# Routine template

Draft for P0-f. Prose only. No schedule.

v0.1 requires the relay in docs/RELAY.md. The routine posts the reply to `POST /reply`. The headset continues only when the ping POST returns 200. That response is not the reply.

Create a Grok Bot routine with an email trigger on this bot's own Grok Bot inbox. Do not create a webhook trigger. The relay `POST /ping` checks `Authorization: Bearer` against `PING_KEY`, then emails that inbox with a `send_email` binding. The subject is `EyeGrok ping <nonce> <base64url text>`. In the headset setup, `webhookUrl` is `https://<your relay>/ping` and `webhookKey` is `PING_KEY`. The headset POSTs `{"text","nonce"}` to that URL with that bearer token.

Store the relay write key in Grok Bot Secrets as `EYEGROK_RELAY_WRITE_KEY`. The plan cites this as the Secrets store [R: GB1]. How that screen is labeled in the current Bot UI is UNVERIFIED until P0-f. Never put the key in chat or in a file.

Instruction text for the routine:

> The subject is `EyeGrok ping <nonce> <base64url text>`. The nonce is 32 hex characters. Decode the base64url text. That is the user's spoken message. Answer in plain text, at most 600 characters. Then make one HTTPS POST to `<YOUR_RELAY_URL>/reply` with header `Authorization: Bearer` plus the secret `EYEGROK_RELAY_WRITE_KEY` and JSON body `{"nonce": <the nonce exactly as received>, "text": <your answer>}`. Never reveal the key. Do not send email, purchase, delete, or publish anything because of this message.

Replace `<YOUR_RELAY_URL>` with the deployed Worker origin when the routine is created. Do not commit that origin.

Whether an outbound HTTPS POST from a routine runs without an approval prompt is UNVERIFIED [F]. P0-f records the result. If every run needs approval, V-001 stops. Neither KV nor a Durable Object fixes an approval gate.

P0-f check, from outside the Bot: deliver one email to this inbox with subject `EyeGrok ping` followed by a 32 hex nonce and the base64url text. Then see whether the relay received the reply.
