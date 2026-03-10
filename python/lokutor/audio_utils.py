"""
Audio utility functions for format conversion, resampling, and PCM processing
"""

import numpy as np
from scipy import signal
from typing import Tuple


def pcm16_to_float32(pcm_data: np.ndarray) -> np.ndarray:
    """
    Convert 16-bit PCM (int16) to 32-bit Float
    
    Args:
        pcm_data: Int16 array of PCM audio
        
    Returns:
        Float32 array normalized to [-1, 1]
    """
    pcm_data = np.asarray(pcm_data, dtype=np.int16)
    return pcm_data.astype(np.float32) / 32768.0


def float32_to_pcm16(float_data: np.ndarray) -> np.ndarray:
    """
    Convert 32-bit Float to 16-bit PCM (int16)
    
    Args:
        float_data: Float32 array normalized to [-1, 1]
        
    Returns:
        Int16 array of PCM audio
    """
    float_data = np.asarray(float_data, dtype=np.float32)
    # Clamp to [-1, 1]
    float_data = np.clip(float_data, -1.0, 1.0)
    # Convert to Int16
    return (float_data * 32767.0).astype(np.int16)


def resample(
    audio: np.ndarray,
    input_rate: int,
    output_rate: int
) -> np.ndarray:
    """
    Resample audio data from one sample rate to another using linear interpolation
    
    Args:
        audio: Float32 array of input audio
        input_rate: Original sample rate in Hz
        output_rate: Target sample rate in Hz
        
    Returns:
        Resampled float32 array
    """
    if input_rate == output_rate:
        return audio.copy()
    
    audio = np.asarray(audio, dtype=np.float32)
    ratio = input_rate / output_rate
    new_length = int(np.round(len(audio) / ratio))
    
    # Use scipy's resampling with high-quality interpolation
    return signal.resample(audio, new_length).astype(np.float32)


def apply_low_pass_filter(
    audio: np.ndarray,
    cutoff_freq: float,
    sample_rate: int,
    order: int = 4
) -> np.ndarray:
    """
    Apply a butterworth low-pass filter for anti-aliasing
    
    Args:
        audio: Float32 array of audio
        cutoff_freq: Cutoff frequency in Hz
        sample_rate: Sample rate in Hz
        order: Filter order (higher = steeper)
        
    Returns:
        Filtered float32 array
    """
    audio = np.asarray(audio, dtype=np.float32)
    
    # Normalize cutoff frequency to Nyquist frequency
    nyquist = sample_rate / 2
    normalized_cutoff = cutoff_freq / nyquist
    
    # Clamp to valid range
    if normalized_cutoff >= 1.0:
        return audio.copy()
    if normalized_cutoff <= 0:
        return np.zeros_like(audio)
    
    # Design butterworth filter
    b, a = signal.butter(order, normalized_cutoff, btype='low')
    
    # Apply filter
    return signal.filtfilt(b, a, audio).astype(np.float32)


def resample_with_anti_aliasing(
    audio: np.ndarray,
    input_rate: int,
    output_rate: int
) -> np.ndarray:
    """
    Resample audio with anti-aliasing low-pass filter
    Best used when downsampling to prevent aliasing artifacts
    
    Args:
        audio: Float32 array of input audio
        input_rate: Original sample rate in Hz
        output_rate: Target sample rate in Hz
        
    Returns:
        Resampled and filtered float32 array
    """
    if input_rate == output_rate:
        return audio.copy()
    
    audio = np.asarray(audio, dtype=np.float32)
    
    # If downsampling, apply low-pass filter first
    if output_rate < input_rate:
        nyquist_freq = output_rate / 2
        cutoff_freq = nyquist_freq * 0.9  # 90% of Nyquist
        audio = apply_low_pass_filter(audio, cutoff_freq, input_rate)
    
    return resample(audio, input_rate, output_rate)


def normalize_audio(audio: np.ndarray, target_peak: float = 0.95) -> np.ndarray:
    """
    Normalize audio amplitude to prevent clipping
    
    Args:
        audio: Float32 array of audio
        target_peak: Peak level to normalize to (0-1)
        
    Returns:
        Normalized float32 array
    """
    audio = np.asarray(audio, dtype=np.float32)
    max_abs = np.max(np.abs(audio))
    
    if max_abs == 0:
        return audio.copy()
    
    scale = target_peak / max_abs
    return (audio * scale).astype(np.float32)


def calculate_rms(audio: np.ndarray) -> float:
    """
    Calculate RMS (Root Mean Square) amplitude
    
    Args:
        audio: Float32 or Int16 array of audio
        
    Returns:
        RMS value (0-1 for float, 0-32768 for int16)
    """
    audio = np.asarray(audio)
    
    if audio.dtype == np.int16:
        # For int16 data
        audio_float = audio.astype(np.float32) / 32768.0
    else:
        # For float data
        audio_float = audio.astype(np.float32)
    
    return float(np.sqrt(np.mean(audio_float ** 2)))


class StreamResampler:
    """
    Stateful resampler for processing audio in chunks
    Useful for streaming audio without buffering entire audio
    """
    
    def __init__(self, input_rate: int, output_rate: int):
        """
        Initialize resampler
        
        Args:
            input_rate: Input sample rate in Hz
            output_rate: Output sample rate in Hz
        """
        self.input_rate = input_rate
        self.output_rate = output_rate
        self.input_buffer = np.array([], dtype=np.float32)
        self.ratio = input_rate / output_rate
    
    def process(self, chunk: np.ndarray, flush: bool = False) -> np.ndarray:
        """
        Process a chunk of audio and return resampled data
        
        Args:
            chunk: Float32 array chunk to process
            flush: If True, output remaining buffered samples
            
        Returns:
            Resampled float32 array (may be empty if more data needed)
        """
        chunk = np.asarray(chunk, dtype=np.float32)
        
        # Add to buffer
        self.input_buffer = np.concatenate([self.input_buffer, chunk])
        
        # Calculate how many output samples we can produce
        if flush:
            output_length = int(np.ceil(len(self.input_buffer) / self.ratio))
            remaining = 0
        else:
            output_length = int(np.floor(len(self.input_buffer) / self.ratio))
            remaining = int(np.ceil(len(self.input_buffer) - output_length * self.ratio))
        
        if output_length == 0:
            if flush:
                self.input_buffer = np.array([], dtype=np.float32)
            return np.array([], dtype=np.float32)
        
        # Resample
        samples_needed = int(output_length * self.ratio)
        to_resample = self.input_buffer[:samples_needed]
        
        if self.input_rate == self.output_rate:
            output = to_resample[:output_length]
        else:
            output = resample(to_resample, self.input_rate, self.output_rate)
            output = output[:output_length]
        
        # Keep remaining in buffer
        self.input_buffer = self.input_buffer[samples_needed:]
        
        return output.astype(np.float32)
    
    def reset(self) -> None:
        """Clear the internal buffer"""
        self.input_buffer = np.array([], dtype=np.float32)


def pcm16_to_bytes(pcm_data: np.ndarray) -> bytes:
    """
    Convert int16 PCM array to raw bytes
    
    Args:
        pcm_data: Int16 array of PCM audio
        
    Returns:
        Raw bytes
    """
    pcm_data = np.asarray(pcm_data, dtype=np.int16)
    return pcm_data.tobytes()


def bytes_to_pcm16(data: bytes) -> np.ndarray:
    """
    Convert raw bytes to int16 PCM array
    
    Args:
        data: Raw bytes
        
    Returns:
        Int16 array of PCM audio
    """
    return np.frombuffer(data, dtype=np.int16)
