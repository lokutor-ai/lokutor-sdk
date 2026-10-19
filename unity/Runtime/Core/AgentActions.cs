using System;
using System.Collections.Generic;
using System.Globalization;

namespace Lokutor
{
    /// <summary>The type of one action parameter, as the language model sees it.</summary>
    public enum ParamType { String, Number, Integer, Boolean }

    /// <summary>One argument an action takes, e.g. new ActionParam("item", "Which item to give").</summary>
    public sealed class ActionParam
    {
        public string Name { get; }
        public string Description { get; }
        public ParamType Type { get; }
        public bool Required { get; }
        /// <summary>If set, the model may only pick one of these values.</summary>
        public string[] Choices { get; }

        public ActionParam(string name, string description, ParamType type = ParamType.String,
                           bool required = true, params string[] choices)
        {
            Name = name;
            Description = description ?? "";
            Type = type;
            Required = required;
            Choices = choices != null && choices.Length > 0 ? choices : null;
        }
    }

    /// <summary>The arguments the character chose when it called an action.</summary>
    public sealed class ActionArgs
    {
        readonly Dictionary<string, object> values;
        public string Json { get; }

        public ActionArgs(string json)
        {
            Json = string.IsNullOrEmpty(json) ? "{}" : json;
            try { values = MiniJson.Parse(Json) as Dictionary<string, object>; }
            catch (FormatException) { values = null; }
            if (values == null) values = new Dictionary<string, object>();
        }

        public bool Has(string name) => values.ContainsKey(name) && values[name] != null;

        public string GetString(string name, string fallback = "")
        {
            if (!values.TryGetValue(name, out var v) || v == null) return fallback;
            return v as string ?? Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public double GetNumber(string name, double fallback = 0)
        {
            if (!values.TryGetValue(name, out var v) || v == null) return fallback;
            if (v is double d) return d;
            return double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Float,
                                   CultureInfo.InvariantCulture, out var p) ? p : fallback;
        }

        public int GetInt(string name, int fallback = 0) => (int)Math.Round(GetNumber(name, fallback));

        public bool GetBool(string name, bool fallback = false)
        {
            if (!values.TryGetValue(name, out var v) || v == null) return fallback;
            if (v is bool b) return b;
            return bool.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out var p) ? p : fallback;
        }

        public override string ToString() => Json;
    }

    /// <summary>
    /// A game function the character can decide to call: open a door, give an item, start a quest.
    /// The handler returns a short sentence that tells the character what happened ("Gave the iron
    /// sword. The player now has 5 gold."), which it uses to answer.
    /// </summary>
    public sealed class AgentAction
    {
        public string Name { get; }
        public string Description { get; }
        public IReadOnlyList<ActionParam> Parameters { get; }
        internal Func<ActionArgs, string> Handler { get; }

        public AgentAction(string name, string description, Func<ActionArgs, string> handler, params ActionParam[] parameters)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("An action needs a name.");
            foreach (char c in name)
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-'))
                    throw new ArgumentException("Action names may only use letters, digits, '_' and '-': " + name);
            Name = name;
            Description = description ?? "";
            Handler = handler ?? throw new ArgumentNullException(nameof(handler));
            Parameters = parameters ?? new ActionParam[0];
        }

        /// <summary>The OpenAI-style function definition the server expects in "tools".</summary>
        internal Dictionary<string, object> ToToolDefinition()
        {
            var props = new Dictionary<string, object>();
            var required = new List<object>();
            foreach (var p in Parameters)
            {
                var schema = new Dictionary<string, object>
                {
                    ["type"] = p.Type == ParamType.String ? "string"
                             : p.Type == ParamType.Integer ? "integer"
                             : p.Type == ParamType.Boolean ? "boolean" : "number",
                    ["description"] = p.Description,
                };
                if (p.Choices != null) schema["enum"] = new List<object>(p.Choices);
                props[p.Name] = schema;
                if (p.Required) required.Add(p.Name);
            }
            return new Dictionary<string, object>
            {
                ["type"] = "function",
                ["function"] = new Dictionary<string, object>
                {
                    ["name"] = Name,
                    ["description"] = Description,
                    ["parameters"] = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = props,
                        ["required"] = required,
                    },
                },
            };
        }
    }
}
