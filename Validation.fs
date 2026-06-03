module Validacao

open Domain

// ---------------------------------------------------------------------------
// Validação pura – sem I/O, sem efeitos colaterais, totalmente testável
// ---------------------------------------------------------------------------

let private naoVazio (nome: string) (valor: string) : Result<string, ErroValidacao> =
    if System.String.IsNullOrWhiteSpace valor
    then Error (CampoAusente nome)
    else Ok valor

let private valorCorreto (esperado: string) (valor: string) : Result<string, ErroValidacao> =
    if valor = esperado
    then Ok valor
    else Error (ValorInvalido valor)

let private moedaValida (esperada: string) (moeda: string) : Result<string, ErroValidacao> =
    if moeda = esperada
    then Ok moeda
    else Error (MoedaInvalida moeda)

// ---------------------------------------------------------------------------
// Pipeline de validação – campos obrigatórios primeiro, depois valores
// Nota: transaction_id é opcional (ausência não cancela)
// ---------------------------------------------------------------------------

let validarPayload (cfg: ConfigWebhook) (p: PayloadPagamento) : Result<PayloadPagamento, ErroValidacao> =
    naoVazio "event"     p.Evento
    |> Result.bind (fun _ -> naoVazio "amount"    p.Valor)
    |> Result.bind (fun _ -> naoVazio "currency"  p.Moeda)
    |> Result.bind (fun _ -> naoVazio "timestamp" p.Timestamp)
    |> Result.bind (fun _ -> valorCorreto cfg.ValorEsperado p.Valor)
    |> Result.bind (fun _ -> moedaValida  cfg.MoedaEsperada p.Moeda)
    |> Result.map  (fun _ -> p)

let descreverErro (erro: ErroValidacao) : string =
    match erro with
    | CampoAusente   nome -> sprintf "missing field: %s" nome
    | ValorInvalido  v    -> sprintf "mismatch"
    | EventoInvalido e    -> sprintf "invalid event: %s" e
    | MoedaInvalida  m    -> sprintf "mismatch"