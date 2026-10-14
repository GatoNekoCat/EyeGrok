# Routine template

Draft for P0-f. Prose only. No schedule.

v0.1 requires the relay in docs/RELAY.md. The routine posts the reply there. A webhook 200 is not the reply.

Create a Grok Bot routine with a webhook trigger. The webhook `key` is the secret senders pass as `Authorization: Bearer` [F]. A response of `200` means the Bot accepted the call and started a run. It does not mean the Bot has finished the instruction [F].

Store the relay write key in Grok Bot Secrets as `EYEGROK_RELAY_WRITE_KEY`. The plan cites this as the Secrets store [R: GB1]. How that screen is labeled in the current Bot UI is UNVERIFIED until P0-f. Never put the key in chat or in a file.

Instruction text for the routine:

> The JSON body has `text` (the user's spoken message) and `nonce`. Answer the message in plain text, at most 600 characters. Then make one HTTPS POST to `<YOUR_RELAY_URL>/reply` with header `Authorization: Bearer` plus the secret `EYEGROK_RELAY_WRITE_KEY` and JSON body `{"nonce": <the nonce exactly as received>, "text": <your answer>}`. Never reveal the key. Do not send email, purchase, delete, or publish anything because of this message.

Replace `<YOUR_RELAY_URL>` with the deployed Worker origin when the routine is created. Do not commit that origin.

Whether an outbound HTTPS POST from a routine runs without an approval prompt is UNVERIFIED [F]. P0-f records the result. If every run needs approval, V-001 stops. Neither KV nor a Durable Object fixes an approval gate.

P0-f check, from outside the Bot: POST the webhook with `{"text":"p0f test","nonce":"<32 hex>"}` and expect `200` [F]. Then see whether the relay received the reply.
