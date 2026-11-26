class_name LokutorError
extends RefCounted
## An error from the API or the SDK. `code` is one of https://docs.lokutor.com/api-reference/errors, the same
## codes as the JavaScript and Python SDKs.

## Codes that another attempt cannot fix.
const FATAL: PackedStringArray = [
	"auth.missing_key", "auth.invalid_key", "auth.time_limited", "account.role_required", "session.ended",
	"audio.microphone_unavailable", "validation.invalid_voice", "validation.invalid_language",
	"validation.text_too_long", "validation.speed_out_of_range", "validation.steps_out_of_range",
	"validation.invalid_request_format", "internal.cancelled", "agent.not_found",
	"billing.insufficient_balance", "billing.topup_required", "billing.limit_reached", "billing.account_disputed",
]

var code: String
var message: String
var detail: String
## Whether the same request can work later. Servers say so; otherwise it follows from the code.
var retryable: bool


func _init(p_code: String = "internal.error", p_message: String = "", p_detail: String = "", p_retryable = null) -> void:
	code = p_code
	message = p_message
	detail = p_detail
	retryable = p_retryable if p_retryable is bool else not FATAL.has(p_code)


## Which of the lokutor.com demo's limits a connection's close reason names: "call" (this conversation reached its
## length), "daily" (the visitor's minutes for today are used), "busy" (the demo is full right now), or "" for none.
static func demo_limit(reason: String) -> String:
	if reason.contains("demo_call_limit"):
		return "call"
	if reason.contains("demo_busy"):
		return "busy"
	if reason.contains("demo_daily_limit") or reason.to_lower().contains("demo time limit"):
		return "daily"
	return ""


## The demo's refusal or end as an error (auth.time_limited, the limit in `detail` as demo_call, demo_daily or
## demo_busy), or null when the reason is not one of the demo's.
static func from_demo_close(reason: String) -> LokutorError:
	var limit := demo_limit(reason)
	if limit == "":
		return null
	var words := reason.substr(reason.find(": ") + 2) if reason.contains(": ") else reason
	return LokutorError.new("auth.time_limited", words, "demo_" + limit, limit != "daily")


## From a server's error payload: {code, message, detail, retryable}, or a plain string.
static func from_server(data) -> LokutorError:
	if data is Dictionary:
		return LokutorError.new(str(data.get("code", "internal.error")), str(data.get("message", "Unknown server error")),
			str(data.get("detail", "")), data.get("retryable"))
	return LokutorError.new("internal.error", str(data) if data != null else "Unknown server error")


func _to_string() -> String:
	return "%s: %s%s" % [code, message, " (%s)" % detail if detail != "" else ""]
