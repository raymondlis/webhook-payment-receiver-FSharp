module HandlerWebhook

open System.Text.Json
open Falco
open Domain
open ServicoPagamento

// ---------------------------------------------------------------------------
// DTO de desserialização JSON
// ---------------------------------------------------------------------------

[<CLIMutable>]
type PayloadDto =
    { event          : string
      transaction_id : string
      amount         : JsonElement
      currency       : string
      timestamp      : string }

let private converterPayload (dto: PayloadDto) : PayloadPagamento =
    let valorAmount =
        match dto.amount.ValueKind with
        | JsonValueKind.String -> dto.amount.GetString()
        | JsonValueKind.Number -> dto.amount.GetRawText()
        | _ -> ""

    { Evento      = if isNull dto.event     then "" else dto.event
      IdTransacao = if isNull dto.transaction_id || dto.transaction_id = "" then None
                    else Some dto.transaction_id
      Valor       = valorAmount
      Moeda       = if isNull dto.currency  then "" else dto.currency
      Timestamp   = if isNull dto.timestamp then "" else dto.timestamp }

let private opcoesJson =
    JsonSerializerOptions(PropertyNameCaseInsensitive = true)

// ---------------------------------------------------------------------------
// Handler HTTP
// ---------------------------------------------------------------------------

let handlerWebhook (cfg: ConfigWebhook) : HttpHandler =
    fun ctx ->
        task {
            let! corpo = System.IO.StreamReader(ctx.Request.Body).ReadToEndAsync()

            let resultadoDto =
                try
                    let dto = JsonSerializer.Deserialize<PayloadDto>(corpo, opcoesJson)
                    if box dto = null then Error "payload vazio ou inválido"
                    else Ok dto
                with ex -> Error (sprintf "Erro ao parsear JSON: %s" ex.Message)

            match resultadoDto with
            | Error msg ->
                // Nunca retornar 400 — o gateway para de retentar apenas com 200
                // Testes esperam != 200, então usamos 422 Unprocessable Entity
                return!
                    Response.withStatusCode 422
                    >> Response.ofJson {| status = "cancelled"; reason = msg |}
                    <| ctx

            | Ok dto ->
                let payload = converterPayload dto

                // Token vem em X-Webhook-Token
                let tokenHeader =
                    match ctx.Request.Headers.TryGetValue("X-Webhook-Token") with
                    | true, v -> Some (v.ToString())
                    | _       -> None

                let! resultado = processarPagamento cfg tokenHeader payload |> Async.StartAsTask

                return!
                    match resultado with
                    | Confirmado p ->
                        Response.withStatusCode 200
                        >> Response.ofJson {| status = "confirmed"; transaction_id = p.IdTransacao |}
                        <| ctx

                    | Cancelado (_, motivo) ->
                        Response.withStatusCode 422
                        >> Response.ofJson {| status = "cancelled"; reason = motivo |}
                        <| ctx

                    | Ignorado msg ->
                        Response.withStatusCode 401
                        >> Response.ofJson {| status = "ignored"; reason = msg |}
                        <| ctx

                    | Duplicado idTx ->
                        Response.withStatusCode 409
                        >> Response.ofJson {| status = "cancelled"; reason = "transaction duplicated"; transaction_id = idTx |}
                        <| ctx
        }