# Exemplo de integração Vapora → Document Validation Service v1

Este exemplo é intencionalmente desacoplado: Vapora atua como consumidor e o DVS não recebe regras internas de elegibilidade de agenda, portaria ou carga.

## Chamada recomendada

A chamada deve sair do backend do Vapora ou de gateway confiável. **Nunca exponha a API key do DVS em JavaScript entregue ao navegador.**

```javascript
// Exemplo Node.js / BFF. A chave deve vir de secret manager.
async function validateVehicleInspection({ fileBase64, driverId, vehiclePlate, operationType }) {
  const response = await fetch(`${process.env.DVS_BASE_URL}/api/v1/validation`, {
    method: 'POST',
    headers: {
      'content-type': 'application/json',
      'x-api-key': process.env.DVS_API_KEY,
      'x-correlation-id': crypto.randomUUID()
    },
    body: JSON.stringify({
      documentType: 'CIPP',
      policy: 'CIPP_DEFAULT',
      idempotencyKey: `vapora-cipp-${driverId}-${vehiclePlate}`,
      context: {
        driverId,
        vehiclePlate,
        operationType,
        cargoType: 'INFLAMMABLE',
        riskClass: '3',
        vehicleType: 'TANQUE',
        site: 'TERMINAL_A'
      },
      file: fileBase64
    })
  });

  if (!response.ok) throw new Error(`DVS returned ${response.status}`);
  return response.json();
}
```

## Tratamento de resultado pelo consumidor

| Resultado do DVS | Ação típica no Vapora |
|---|---|
| `APPROVED` | Registrar o `validationId` e aplicar as próprias regras de elegibilidade. |
| `MANUAL_REVIEW` | Registrar `PENDING_VALIDATION`, encaminhar à revisão humana e não reprovar automaticamente. |
| `REJECTED` | Exibir os códigos de regra; Vapora define a ação operacional correspondente. |
| erro/timeout | Manter estado pendente e aplicar retry com a mesma `idempotencyKey`. |

## Dados que devem ser persistidos pelo Vapora

Armazene somente o necessário: `validationId`, `status`, `score`, `confidence`, `policy`, `policyVersion`, `createdAt`, códigos das regras e correlação. Não é necessário persistir OCR, Base64 ou imagem no Vapora para consumir a decisão.
