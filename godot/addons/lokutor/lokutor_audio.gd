class_name LokutorAudio
extends RefCounted
## Audio helpers, named as in the JavaScript and Python SDKs.


static func pcm16_to_float32(pcm: PackedByteArray) -> PackedFloat32Array:
	var out := PackedFloat32Array()
	out.resize(pcm.size() >> 1)
	for i in out.size():
		out[i] = pcm.decode_s16(i * 2) / 32768.0
	return out


static func float32_to_pcm16(samples: PackedFloat32Array) -> PackedByteArray:
	var out := PackedByteArray()
	out.resize(samples.size() * 2)
	for i in samples.size():
		out.encode_s16(i * 2, int(clampf(samples[i], -1.0, 1.0) * 32767.0))
	return out


static func calculate_rms(samples: PackedFloat32Array) -> float:
	if samples.is_empty():
		return 0.0
	var sum := 0.0
	for v in samples:
		sum += v * v
	return sqrt(sum / samples.size())


static func normalize_audio(samples: PackedFloat32Array, target_peak := 0.95) -> PackedFloat32Array:
	var peak := 0.0
	for v in samples:
		peak = maxf(peak, absf(v))
	if peak < 1e-5:
		return samples
	var out := samples.duplicate()
	for i in out.size():
		out[i] *= target_peak / peak
	return out


static func resample(samples: PackedFloat32Array, from_rate: int, to_rate: int) -> PackedFloat32Array:
	return StreamResampler.new(from_rate, to_rate).process(samples)


## A whole PCM16 mono recording as a WAV file's bytes.
static func wav_bytes(pcm: PackedByteArray, rate: int) -> PackedByteArray:
	var b := StreamPeerBuffer.new()
	b.put_data("RIFF".to_ascii_buffer())
	b.put_u32(36 + pcm.size())
	b.put_data("WAVEfmt ".to_ascii_buffer())
	b.put_u32(16)
	b.put_u16(1)
	b.put_u16(1)
	b.put_u32(rate)
	b.put_u32(rate * 2)
	b.put_u16(2)
	b.put_u16(16)
	b.put_data("data".to_ascii_buffer())
	b.put_u32(pcm.size())
	b.put_data(pcm)
	return b.data_array


## Streaming mono resampler: audio can be pushed in blocks of any size and its phase is kept across them.
## Downsampling averages the input over each output step, which is enough anti-aliasing for speech.
class StreamResampler extends RefCounted:
	var from_rate: int
	var to_rate: int
	var _pos := 0.0
	var _carry := PackedFloat32Array()

	func _init(p_from: int, p_to: int) -> void:
		from_rate = p_from
		to_rate = p_to

	func reset() -> void:
		_pos = 0.0
		_carry = PackedFloat32Array()

	func process(input: PackedFloat32Array) -> PackedFloat32Array:
		if from_rate == to_rate:
			return input
		var buf := _carry + input
		var step := float(from_rate) / to_rate
		var out := PackedFloat32Array()
		out.resize(maxi(0, int((buf.size() - _pos) / step) + 1))
		var n := 0
		var pos := _pos
		if step > 1.0:
			while pos + step <= buf.size():
				var a := int(pos)
				var b := int(pos + step)
				var sum := 0.0
				for k in range(a, b):
					sum += buf[k]
				out[n] = sum / maxi(1, b - a)
				n += 1
				pos += step
		else:
			while pos + 1.0 < buf.size():
				var a := int(pos)
				var t := pos - a
				out[n] = buf[a] * (1.0 - t) + buf[a + 1] * t
				n += 1
				pos += step
		var keep := mini(int(pos), buf.size())
		_carry = buf.slice(keep)
		_pos = pos - keep
		out.resize(n)
		return out
