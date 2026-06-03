module Domain

open System

// ---------------------------------------------------------------------------
// Tipos do domínio
// ---------------------------------------------------------------------------

type PayloadPagamento =
    { Evento      : string
      IdTransacao : string option
      Valor       : string
      Moeda       : string
      Timestamp   : string }

type ErroValidacao =
    | CampoAusente   of string
    | ValorInvalido  of string
    | EventoInvalido of string
    | MoedaInvalida  of string

type ResultadoPagamento =
    | Confirmado of PayloadPagamento
    | Cancelado  of PayloadPagamento * string
    | Ignorado   of string
    | Duplicado  of string

// ---------------------------------------------------------------------------
// Configuração
// ---------------------------------------------------------------------------

type ConfigWebhook =
    { TokenEsperado  : string
      UrlConfirmacao : string
      UrlCancelamento: string
      ValorEsperado  : string   // ex: "49.90"
      MoedaEsperada  : string   // ex: "BRL"
      CaminhoBanco   : string }

let carregarConfig () : ConfigWebhook =
    let env chave padrao =
        let v = Environment.GetEnvironmentVariable chave
        if String.IsNullOrWhiteSpace v then padrao else v
    { TokenEsperado   = env "WEBHOOK_SECRET"    "meu-token-secreto"
      UrlConfirmacao  = env "CONFIRM_URL"        "http://127.0.0.1:5001/confirmar"
      UrlCancelamento = env "CANCEL_URL"         "http://127.0.0.1:5001/cancelar"
      ValorEsperado   = env "EXPECTED_AMOUNT"    "49.90"
      MoedaEsperada   = env "EXPECTED_CURRENCY"  "BRL"
      CaminhoBanco    = env "DB_PATH"            "transacoes.db" }