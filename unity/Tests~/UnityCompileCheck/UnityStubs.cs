// Declarations of the Unity APIs the package uses, with the signatures from the Unity scripting
// reference (2021.3 - 6000.x). Bodies are irrelevant: this only lets the compiler check our calls.
#pragma warning disable CS0067, CS0649, CS0414
using System;
namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; }
        public static void Destroy(Object obj) { }
        public static implicit operator bool(Object o) => o != null;
    }
    public class Component : Object
    {
        public GameObject gameObject => null;
        public Transform transform => null;
        public T GetComponent<T>() => default;
    }
    public class Behaviour : Component { public bool enabled { get; set; } public bool isActiveAndEnabled => true; }
    public class MonoBehaviour : Behaviour { }
    public enum PrimitiveType { Sphere, Capsule, Cylinder, Cube, Plane, Quad }
    public class GameObject : Object
    {
        public Transform transform => null;
        public T GetComponent<T>() => default;
        public T AddComponent<T>() where T : Component => default;
        public void SetActive(bool v) { }
        public static GameObject CreatePrimitive(PrimitiveType type) => null;
    }
    public class Camera : Behaviour { public static Camera main => null; }
    public struct Rect { public Rect(float x, float y, float w, float h) { } }
    public static class Screen { public static int width => 0; public static int height => 0; }
    public class GUIStyle { public GUIStyle(GUIStyle other) { } public int fontSize { get; set; } public bool wordWrap { get; set; } }
    public class GUISkin { public GUIStyle label => null; }
    public static class GUI { public static GUISkin skin => null; }
    public static class GUILayout
    {
        public static void BeginArea(Rect r) { }
        public static void EndArea() { }
        public static void Label(string text, GUIStyle style) { }
        public static void FlexibleSpace() { }
    }
    public class Transform : Component
    {
        public Quaternion localRotation { get; set; }
        public Vector3 localPosition { get; set; }
        public Vector3 localScale { get; set; }
        public void SetParent(Transform parent, bool worldPositionStays) { }
        public Vector3 position { get; set; }
        public Quaternion rotation { get; set; }
        public void LookAt(Vector3 p) { }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 up => default;
        public static Vector3 operator -(Vector3 a, Vector3 b) => default;
        public static Vector3 operator +(Vector3 a, Vector3 b) => default;
        public static float Distance(Vector3 a, Vector3 b) => 0;
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => default;
    }
    public struct Quaternion
    {
        public static Quaternion Euler(float x, float y, float z) => default;
        public static Quaternion operator *(Quaternion a, Quaternion b) => default;
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) => default;
        public static Quaternion LookRotation(Vector3 forward) => default;
    }
    public struct Color { public Color(float r, float g, float b) { } public static Color white => default; }
    public static class Mathf
    {
        public static float Clamp01(float v) => v;
        public static int Clamp(int v, int min, int max) => v;
        public static float Clamp(float v, float min, float max) => v;
        public static float Lerp(float a, float b, float t) => a;
        public static int Max(int a, int b) => a;
    }
    public static class Time { public static float deltaTime => 0; public static float time => 0; }
    public static class Debug
    {
        public static void Log(object m) { }
        public static void Log(object m, Object ctx) { }
        public static void LogWarning(object m, Object ctx) { }
        public static void LogError(object m, Object ctx) { }
    }
    public static class Application { public static string unityVersion => ""; }
    public sealed class AudioSettings
    {
        public static int outputSampleRate => 48000;
        public static void GetDSPBufferSize(out int bufferLength, out int numBuffers) { bufferLength = 1024; numBuffers = 4; }
    }
    public class AudioClip : Object
    {
        public delegate void PCMReaderCallback(float[] data);
        public int samples => 0;
        public int channels => 1;
        public int frequency => 0;
        public static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool stream) => null;
        public bool SetData(float[] data, int offsetSamples) => true;
        public bool GetData(float[] data, int offsetSamples) => true;
    }
    public class AudioSource : Behaviour
    {
        public AudioClip clip { get; set; }
        public bool loop { get; set; }
        public bool playOnAwake { get; set; }
        public float spatialBlend { get; set; }
        public float dopplerLevel { get; set; }
        public float minDistance { get; set; }
        public float maxDistance { get; set; }
        public int timeSamples { get; set; }
        public bool isPlaying => false;
        public void Play() { }
        public void Stop() { }
    }
    public sealed class Microphone
    {
        public static string[] devices => new string[0];
        public static AudioClip Start(string deviceName, bool loop, int lengthSec, int frequency) => null;
        public static void End(string deviceName) { }
        public static int GetPosition(string deviceName) => 0;
        public static void GetDeviceCaps(string deviceName, out int minFreq, out int maxFreq) { minFreq = maxFreq = 0; }
    }
    public class Mesh : Object { public int GetBlendShapeIndex(string blendShapeName) => -1; }
    public class Renderer : Component { }
    public class SkinnedMeshRenderer : Renderer
    {
        public Mesh sharedMesh { get; set; }
        public void SetBlendShapeWeight(int index, float value) { }
    }
    public enum KeyCode { None = 0, Space = 32, E = 101, T = 116 }
    public static class Input { public static bool GetKey(KeyCode key) => false; public static bool GetKeyDown(KeyCode key) => false; }
    public class Collider : Component { public bool CompareTag(string tag) => false; }
    [AttributeUsage(AttributeTargets.Field)] public sealed class HeaderAttribute : Attribute { public HeaderAttribute(string h) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class TooltipAttribute : Attribute { public TooltipAttribute(string t) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class TextAreaAttribute : Attribute { public TextAreaAttribute(int min, int max) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class RangeAttribute : Attribute { public RangeAttribute(float min, float max) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class AddComponentMenu : Attribute { public AddComponentMenu(string m) { } }
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)] public sealed class RequireComponent : Attribute { public RequireComponent(Type t) { } }
}
namespace UnityEngine.Events
{
    public class UnityEventBase { }
    public class UnityEvent : UnityEventBase { public void Invoke() { } public void AddListener(Action a) { } }
    public class UnityEvent<T0> : UnityEventBase { public void Invoke(T0 a) { } public void AddListener(Action<T0> a) { } }
}
namespace UnityEngine.InputSystem
{
    public class InputAction { public bool enabled => false; public void Enable() { } public bool IsPressed() => false; }
    public class InputActionReference : UnityEngine.Object { public InputAction action => null; }
}
