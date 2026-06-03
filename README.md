# webhook-payment-receiver

Serviço HTTP de webhook escrito em **F#** que recebe notificações de pagamento de um gateway (ex.: MercadoPago, PayPal) e as processa seguindo os princípios da programação funcional: validação pura, tipos imutáveis e pipelines compostos.

---

## Funcionalidades

| Requisito | Status |
|---|---|
| Endpoint HTTP POST em `/webhook` | ✅ |
| Verificação de integridade do payload | ✅ |
| Autenticação por token Bearer | ✅ |
| Idempotência (detecção de duplicatas) | ✅ |
| Callback de confirmação em caso de sucesso | ✅ |
| Callback de cancelamento em caso de divergência | ✅ |
| Persistência em SQLite | ✅ |
| Nunca retorna 400 (compatível com gateways) | ✅ |
| Estilo funcional (funções puras, tipos imutáveis, pipeline com Result) | ✅ |

---

## Arquitetura

```
src/
├── Domain.fs          # Tipos e configuração (sem I/O)
├── Validation.fs      # Pipeline de validação pura (Result<_, _>)
├── PaymentService.fs  # Idempotência, SQLite, callbacks HTTP
├── WebhookHandler.fs  # Requisição/resposta HTTP (Falco)
└── Program.fs         # Ponto de entrada / roteamento
```

A **camada de validação é completamente pura**: dado uma config e um payload, retorna `Ok payload` ou `Error motivo` sem nenhum efeito colateral — trivial de testar unitariamente.

Os efeitos colaterais (escrita no banco, chamadas HTTP) ficam nas bordas do pipeline em `PaymentService.fs`.

---

## Pré-requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

---

## Instalação

```bash
git clone https://github.com/<seu-usuario>/webhook-payment-receiver.git
cd webhook-payment-receiver
cp .env.example .env    # edite os valores conforme necessário
```

---

## Como rodar

### Opção 1 – variáveis de ambiente inline

```bash
cd src
WEBHOOK_SECRET=my-secret-token \
CONFIRM_URL=http://localhost:9000/confirm \
CANCEL_URL=http://localhost:9000/cancel \
ASPNETCORE_URLS=http://localhost:5000 \
dotnet run
```

### Opção 2 – carregando o arquivo .env

```bash
export $(grep -v '^#' .env | xargs)
cd src && dotnet run
```

O servidor sobe em `http://localhost:5000` por padrão.

---

## Endpoints

### `POST /webhook`

Recebe um evento de pagamento do gateway.

**Cabeçalhos**
```
Content-Type: application/json
Authorization: Bearer <WEBHOOK_SECRET>
```

**Corpo**
```json
{
  "event": "payment_success",
  "transaction_id": "abc123",
  "amount": 49.90,
  "currency": "BRL",
  "timestamp": "2025-05-11T16:00:00Z"
}
```

**Respostas** — sempre HTTP 200 (gateways reenviam em caso de não-2xx)

| Cenário | Campo `status` |
|---|---|
| Transação válida e nova | `"confirmado"` |
| Transação duplicada | `"duplicado"` |
| Token ausente ou inválido | `"ignorado"` |
| Campos inválidos ou ausentes | `"cancelado"` |

### `GET /health`

Retorna `{"status":"ok"}` — útil para verificações de disponibilidade.

---

## Executando os testes

O script de teste do professor (`test_webhook.py`) requer `fastapi`, `uvicorn` e `requests`.

```bash
pip install fastapi uvicorn requests

# Terminal 1 – subir o servidor
export $(grep -v '^#' .env | xargs) && cd src && dotnet run

# Terminal 2 – rodar os testes
python test_webhook.py
```

É possível sobrescrever os valores padrão do payload:

```bash
python test_webhook.py --amount 99.90 --currency BRL --token my-secret-token
```

---

## Referência de configuração

Toda a configuração é lida de variáveis de ambiente (valores padrão abaixo):

| Variável | Padrão | Descrição |
|---|---|---|
| `WEBHOOK_SECRET` | `my-secret-token` | Token secreto para autenticação Bearer |
| `CONFIRM_URL` | `http://localhost:9000/confirm` | Chamada em pagamento confirmado |
| `CANCEL_URL` | `http://localhost:9000/cancel` | Chamada em pagamento cancelado |
| `EXPECTED_CURRENCY` | `BRL` | Moeda aceita pelo serviço |
| `DB_PATH` | `transacoes.db` | Caminho do arquivo SQLite |
| `ASPNETCORE_URLS` | `http://localhost:5000` | Endereço de escuta |

---

## Destaques de programação funcional

- **Uniões discriminadas** (`ResultadoPagamento`, `ErroValidacao`) modelam explicitamente todos os resultados possíveis — sem nulos, sem exceções para lógica de negócio.
- **Pipeline com Result** em `Validation.fs` encadeia verificações com `Result.bind`, curto-circuitando na primeira falha.
- **Núcleo puro / borda impura**: `Domain.fs` e `Validation.fs` não fazem nenhum I/O; os efeitos ficam isolados em `PaymentService.fs`.
- **Records imutáveis** para `PayloadPagamento` e `ConfigWebhook` — o estado nunca é mutado.
- **Workflows assíncronos** (`async { }`) para callbacks HTTP não-bloqueantes.

---

