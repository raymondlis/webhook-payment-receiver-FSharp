module Program

open Falco
open Falco.Routing
open Falco.HostBuilder
open Domain
open HandlerWebhook

[<EntryPoint>]
let main args =
    let cfg = carregarConfig ()

    ServicoPagamento.iniciarBanco cfg.CaminhoBanco

    printfn "=== Receptor de Pagamentos via Webhook ==="
    printfn "Token secreto    : %s" (String.replicate (cfg.TokenEsperado.Length) "*")
    printfn "URL confirmação  : %s" cfg.UrlConfirmacao
    printfn "URL cancelamento : %s" cfg.UrlCancelamento
    printfn "Moeda esperada   : %s" cfg.MoedaEsperada
    printfn "Banco de dados   : %s" cfg.CaminhoBanco
    printfn "=========================================="

    webHost args {
        endpoints [
            post "/webhook" (handlerWebhook cfg)
            get  "/health"  (Response.ofJson {| status = "ok" |})
        ]
    }
    0
