using System;
using System.Collections.Generic;

namespace Lokutor
{
    /// <summary>
    /// An error from the API or the SDK. Code is one of https://docs.lokutor.com/api-reference/errors, the same codes as
    /// the JavaScript and Python SDKs.
    /// </summary>
    public sealed class LokutorError
    {
        // Codes that another attempt cannot fix.
        static readonly HashSet<string> Fatal = new HashSet<string>
        {
            "auth.missing_key", "auth.invalid_key", "auth.time_limited", "account.role_required", "session.ended",
            "audio.microphone_unavailable", "validation.invalid_voice", "validation.invalid_language",
            "validation.text_too_long", "validation.speed_out_of_range", "validation.steps_out_of_range",
            "validation.invalid_request_format", "internal.cancelled", "agent.not_found",
            "billing.insufficient_balance", "billing.topup_required", "billing.limit_reached", "billing.account_disputed",
        };

        public string Code { get; }
        public string Message { get; }
        public string Detail { get; }
        /// <summary>Whether the same request can work later. Servers say so; otherwise it follows from the code.</summary>
        public bool Retryable { get; }

        public LokutorError(string code, string message, string detail = "", bool? retryable = null)
        {
            Code = string.IsNullOrEmpty(code) ? "internal.error" : code;
            Message = message ?? "";
            Detail = detail ?? "";
            Retryable = retryable ?? !Fatal.Contains(Code);
        }

        /// <summary>From a server's error payload: {code, message, detail, retryable}, or a plain string.</summary>
        public static LokutorError FromServer(object data)
        {
            if (data is Dictionary<string, object> d)
                return new LokutorError(Json.Str(d, "code", "internal.error"), Json.Str(d, "message", "Unknown server error"), Json.Str(d, "detail"),
                    d.TryGetValue("retryable", out var r) && r is bool b ? b : (bool?)null);
            return new LokutorError("internal.error", data as string ?? "Unknown server error");
        }

        public override string ToString() => Code + ": " + Message + (Detail.Length > 0 ? " (" + Detail + ")" : "");
    }

    /// <summary>A LokutorError thrown, by the calls that have no other way to report one.</summary>
    public sealed class LokutorException : Exception
    {
        public LokutorError Error { get; }
        public LokutorException(LokutorError error, Exception inner = null) : base(error.ToString(), inner) { Error = error; }
    }
}
