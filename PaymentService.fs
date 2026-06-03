module ServicoPagamento

open System
open System.Net.Http
open System.Text
open System.Text.Json
open Microsoft.Data.Sqlite
open Domain

// ---------------------------------------------------------------------------
// Persistência SQLite  (garantia de idempotência)
// ---------------------------------------------------------------------------

let iniciarBanco (caminho: string) =
    use conn = new SqliteConnection(sprintf "Data Source=%s" caminho)
    conn.Open()
    let cmd = conn.CreateCommand()
    cmd.CommandText <-
        """CREATE TABLE IF NOT EXISTS transacoes (
               id           TEXT PRIMARY KEY,
               evento       TEXT NOT NULL,
               valor        TEXT NOT NULL,
               moeda        TEXT NOT NULL,
               timestamp    TEXT NOT NULL,
               recebido_em  TEXT NOT NULL,
               status       TEXT NOT NULL
           )"""
    cmd.ExecuteNonQuery() |> ignore

let private transacaoExiste (caminho: string) (idTx: string) : bool =
    use conn = new SqliteConnection(sprintf "Data Source=%s" caminho)
    conn.Open()
    let cmd = conn.CreateCommand()
    cmd.CommandText <- "SELECT COUNT(1) FROM transacoes WHERE id = $id AND status = 'confirmado'"
    cmd.Parameters.AddWithValue("$id", idTx) |> ignore
    let qtd = cmd.ExecuteScalar() :?> int64
    qtd > 0L

let private persistirTransacao (caminho: string) (p: PayloadPagamento) (status: string) =
    use conn = new SqliteConnection(sprintf "Data Source=%s" caminho)
    conn.Open()
    let cmd = conn.CreateCommand()
    cmd.CommandText <-
        """INSERT OR IGNORE INTO transacoes
               (id, evento, valor, moeda, timestamp, recebido_em, status)
           VALUES ($id, $evento, $valor, $moeda, $timestamp, $agora, $status)"""
    cmd.Parameters.AddWithValue("$id",        p.IdTransacao |> Option.defaultValue (Guid.NewGuid().ToString())) |> ignore
    cmd.Parameters.AddWithValue("$evento",    p.Evento)     |> ignore
    cmd.Parameters.AddWithValue("$valor",     p.Valor)      |> ignore
    cmd.Parameters.AddWithValue("$moeda",     p.Moeda)      |> ignore
    cmd.Parameters.AddWithValue("$timestamp", p.Timestamp)  |> ignore
    cmd.Parameters.AddWithValue("$agora",     DateTime.UtcNow.ToString("o")) |> ignore
    cmd.Parameters.AddWithValue("$status",    status)       |> ignore
    cmd.ExecuteNonQuery() |> ignore

// ---------------------------------------------------------------------------
// Callbacks HTTP
// ---------------------------------------------------------------------------

let private clienteHttp = new HttpClient()

let private chamarCallback (url: string) (idTx: string) : Async<unit> =
    async {
        try
            let corpo = JsonSerializer.Serialize({| transaction_id = idTx |})
            let conteudo = new StringContent(corpo, Encoding.UTF8, "application/json")
            let! resposta = clienteHttp.PostAsync(url, conteudo) |> Async.AwaitTask
            printfn "[callback] POST %s → %d" url (int resposta.StatusCode)
        with ex ->
            printfn "[callback] POST %s falhou: %s" url ex.Message
    }

// ---------------------------------------------------------------------------
// Pipeline principal
// ---------------------------------------------------------------------------

/// Token vem no header X-Webhook-Token
let verificarToken (cfg: ConfigWebhook) (tokenHeader: string option) : bool =
    match tokenHeader with
    | None   -> false
    | Some t -> t.Trim() = cfg.TokenEsperado

let processarPagamento
        (cfg         : ConfigWebhook)
        (tokenHeader : string option)
        (payload     : PayloadPagamento) : Async<ResultadoPagamento> =
    async {
        // 1. Autenticar
        if not (verificarToken cfg tokenHeader) then
            printfn "[webhook] Requisição ignorada – token inválido ou ausente"
            return Ignorado "token inválido"
        else

        // 2. Verificar se transaction_id existe para idempotência
        //    (ausência do campo é permitida mas não gera verificação de duplicata)
        let idTx = payload.IdTransacao |> Option.defaultValue ""

        // 3. Validar campos obrigatórios e valores
        match Validacao.validarPayload cfg payload with
        | Error erro ->
            let motivo = Validacao.descreverErro erro
            printfn "[webhook] Cancelando transação %s – %s" idTx motivo
            persistirTransacao cfg.CaminhoBanco payload "cancelado"
            do! chamarCallback cfg.UrlCancelamento idTx
            return Cancelado (payload, motivo)

        | Ok payloadValido ->

        // 4. Verificar duplicata
        if idTx <> "" && transacaoExiste cfg.CaminhoBanco idTx then
            printfn "[webhook] Transação duplicada %s – cancelando" idTx
            do! chamarCallback cfg.UrlCancelamento idTx
            return Duplicado idTx

        else

        // 5. Confirmar
        printfn "[webhook] Confirmando transação %s" idTx
        persistirTransacao cfg.CaminhoBanco payloadValido "confirmado"
        do! chamarCallback cfg.UrlConfirmacao idTx
        return Confirmado payloadValido
    }