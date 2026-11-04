"""A minimal OpenAI-compatible endpoint that puts ANY agent behind Lokutor's voice pipeline.

Lokutor does the listening and speaking (speech recognition, turn-taking, synthesis, your tools); this
server is the intelligence. Run it anywhere with a public https URL (a cloud service, or a tunnel such as
`cloudflared tunnel --url http://localhost:8000` while developing), then point a Lokutor agent at it:

    VoiceAgentClient(api_key=..., prompt="...", llm={"base_url": "https://YOUR-HOST/v1", "api_key": "..."})

    pip install fastapi uvicorn
    LLM_SERVER_KEY=change-me uvicorn custom_llm_server:app --port 8000

Replace `run_agent` with your own: a LangGraph graph, a LangChain runnable, a call to another API, rules.
It receives the conversation as OpenAI messages and yields the reply as plain text, as it is produced:
Lokutor starts speaking at the first sentence.
"""
import json
import os
import time
import uuid
from typing import AsyncIterator, List

from fastapi import FastAPI, Header, HTTPException, Request
from fastapi.responses import JSONResponse, StreamingResponse

app = FastAPI()
KEY = os.environ.get("LLM_SERVER_KEY", "")


async def run_agent(messages: List[dict]) -> AsyncIterator[str]:
    """Your intelligence. messages[0] is Lokutor's system prompt (the agent's instructions plus how to
    speak on a call); you may use it, extend it, or ignore it. Yield text as it is generated.

    LangGraph, for example (token streaming from the graph's chat model):

        async for chunk, meta in graph.astream({"messages": messages}, stream_mode="messages"):
            if chunk.content and meta.get("langgraph_node") == "agent":
                yield chunk.content
    """
    last = next((m["content"] for m in reversed(messages) if m["role"] == "user"), "")
    for word in f"Entendido. Has dicho: {last}".split(" "):
        yield word + " "


def chunk(cid: str, model: str, delta: dict, finish=None) -> str:
    body = {"id": cid, "object": "chat.completion.chunk", "created": int(time.time()), "model": model,
            "choices": [{"index": 0, "delta": delta, "finish_reason": finish}]}
    return f"data: {json.dumps(body)}\n\n"


@app.post("/v1/chat/completions")
async def chat(req: Request, authorization: str = Header(default="")):
    if KEY and authorization != f"Bearer {KEY}":
        raise HTTPException(status_code=401, detail="bad key")
    body = await req.json()
    messages = body.get("messages", [])
    model = body.get("model") or "custom"
    cid = "chatcmpl-" + uuid.uuid4().hex[:12]

    if not body.get("stream"):
        text = "".join([t async for t in run_agent(messages)])
        return JSONResponse({"id": cid, "object": "chat.completion", "created": int(time.time()), "model": model,
                             "choices": [{"index": 0, "message": {"role": "assistant", "content": text}, "finish_reason": "stop"}]})

    async def stream():
        yield chunk(cid, model, {"role": "assistant"})
        async for piece in run_agent(messages):
            yield chunk(cid, model, {"content": piece})
        yield chunk(cid, model, {}, "stop")
        yield "data: [DONE]\n\n"

    return StreamingResponse(stream(), media_type="text/event-stream")
