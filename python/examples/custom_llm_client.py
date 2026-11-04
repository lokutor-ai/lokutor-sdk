"""Your own model or agent, running in THIS process, behind Lokutor's voice.

Lokutor listens, decides when the caller has finished, speaks, and tells you to stop when the caller
interrupts. Each time the agent has to answer, `on_llm_request` is called with the conversation (OpenAI chat
messages; messages[0] is Lokutor's system prompt) and yields the reply as it is produced. Nothing has to be
reachable from outside: the model and its tools can be a LangGraph on your laptop, a private network, anything
you can call from Python.

    pip install lokutor[sounddevice] langgraph langchain-openai
    LOKUTOR_API_KEY=... OPENAI_API_KEY=... python custom_llm_client.py
"""
import os

from lokutor import VoiceAgentClient, VoiceStyle, Language


def build_agent():
    """Replace with your own. A LangGraph ReAct agent with one tool of yours, for example."""
    from langchain_openai import ChatOpenAI
    from langgraph.prebuilt import create_react_agent

    def order_status(order_id: str) -> str:
        """Look up an order by id."""
        return f"Order {order_id} shipped yesterday."   # your own system, wherever it lives

    return create_react_agent(ChatOpenAI(model="gpt-4o-mini", streaming=True), tools=[order_status])


graph = build_agent()


def on_llm_request(messages, tools):
    # `tools` are Lokutor's tools for this agent (end_call, transfer ...). We ask for none below (llm_tools="none"),
    # so this agent runs only its own.
    for chunk, meta in graph.stream({"messages": messages}, stream_mode="messages"):
        # Only the model's words to the caller: its tokens, not tool results (the node name depends on your graph).
        if getattr(chunk, "content", "") and meta.get("langgraph_node") == "agent":
            yield chunk.content


client = VoiceAgentClient(
    api_key=os.environ["LOKUTOR_API_KEY"],
    prompt="You are the support agent of an online shop. Be brief.",
    voice=VoiceStyle.F1,
    language=Language.ENGLISH,
    on_llm_request=on_llm_request,
    llm_tools="none",
    on_transcription=lambda t: print("caller:", t),
    on_response=lambda t: print("agent:", t),
)
client.start_conversation()
