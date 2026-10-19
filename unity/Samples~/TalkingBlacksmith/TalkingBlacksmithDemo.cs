// Talking blacksmith: add this script to an empty GameObject in any scene and press Play.
// It builds a placeholder character, makes it a Lokutor voice agent, gives it a shop it can sell
// from, and shows subtitles. Put your API key in the field below (or in the LOKUTOR_API_KEY
// environment variable) first. Headphones recommended.
using System.Collections.Generic;
using Lokutor;
using UnityEngine;

public class TalkingBlacksmithDemo : MonoBehaviour
{
    [Tooltip("Your Lokutor API key (dashboard > API keys).")]
    public string apiKey = "";

    [TextArea(3, 8)]
    public string persona =
        "You are Brom, the blacksmith of Oakvale, a gruff but kind dwarf. Speak in one or two short sentences. " +
        "Your shop sells: iron sword (20 gold), steel shield (25 gold), healing potion (5 gold). Nothing else. " +
        "When the player agrees to buy something, call give_item; it takes the gold. Greet the player when they arrive.";

    LokutorAgent brom;
    int gold = 30;
    readonly List<string> bag = new List<string>();
    readonly Dictionary<string, int> prices = new Dictionary<string, int>
    {
        ["iron sword"] = 20, ["steel shield"] = 25, ["healing potion"] = 5,
    };
    string lastPlayer = "", lastBrom = "";

    void Awake()
    {
        // A placeholder body: a capsule with a box for a head. Use your own character instead.
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Brom";
        body.transform.position = new Vector3(0f, 1f, 3f);
        var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
        head.transform.SetParent(body.transform, false);
        head.transform.localPosition = new Vector3(0f, 0.75f, 0f);
        head.transform.localScale = new Vector3(0.6f, 0.45f, 0.6f);
        var jaw = GameObject.CreatePrimitive(PrimitiveType.Cube);
        jaw.transform.SetParent(head.transform, false);
        jaw.transform.localPosition = new Vector3(0f, -0.55f, 0.3f);
        jaw.transform.localScale = new Vector3(0.8f, 0.2f, 0.5f);

        // The whole integration: add the component, describe the character, give it actions.
        body.SetActive(false); // configure before Start() runs
        brom = body.AddComponent<LokutorAgent>();
        brom.apiKey = apiKey;
        brom.persona = persona;
        brom.voice = "M1";
        brom.language = "en";
        brom.jaw = jaw.transform;
        brom.jawMaxAngle = 25f;
        brom.AddAction("give_item", "Sell an item to the player: takes the gold and puts the item in their bag.", args =>
        {
            string item = args.GetString("item");
            if (!prices.TryGetValue(item, out int price)) return "There is no " + item + " in the shop.";
            if (gold < price) return "The player has only " + gold + " gold; the " + item + " costs " + price + ".";
            gold -= price;
            bag.Add(item);
            return "Gave the " + item + ". The player paid " + price + " gold and has " + gold + " left.";
        }, new ActionParam("item", "The item to sell", ParamType.String, true, "iron sword", "steel shield", "healing potion"));
        brom.onPlayerSaid.AddListener(t => lastPlayer = t);
        brom.onCharacterSaid.AddListener(t => lastBrom = t);
        body.SetActive(true);

        var cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(0f, 1.6f, 0f);
            cam.transform.LookAt(new Vector3(0f, 1.5f, 3f));
        }
    }

    void OnGUI()
    {
        var style = new GUIStyle(GUI.skin.label) { fontSize = 20, wordWrap = true };
        GUILayout.BeginArea(new Rect(20, 20, Screen.width - 40, Screen.height - 40));
        GUILayout.Label("Brom is " + StateText(brom != null ? brom.State : AgentState.Disconnected) + "   |   Gold: " + gold +
                        "   |   Bag: " + (bag.Count == 0 ? "empty" : string.Join(", ", bag)), style);
        GUILayout.FlexibleSpace();
        if (lastPlayer.Length > 0) GUILayout.Label("You: " + lastPlayer, style);
        if (lastBrom.Length > 0) GUILayout.Label("Brom: " + lastBrom, style);
        GUILayout.EndArea();
    }

    static string StateText(AgentState s)
    {
        switch (s)
        {
            case AgentState.Connecting: return "arriving...";
            case AgentState.Ready: return "listening (just talk)";
            case AgentState.PlayerTalking: return "hearing you";
            case AgentState.Thinking: return "thinking";
            case AgentState.Speaking: return "talking";
            default: return "away";
        }
    }
}
