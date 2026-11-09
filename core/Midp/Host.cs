using System;

namespace Lumidlet.Midp
{
    public sealed class Glyph
    {
        public int W, H, Advance;
        public byte[] A;
    }

    public interface IAudioClip
    {
        void Play(int loops);
        void Stop();
        void SetVolume(int level);
        void Seek(long ms);
        long DurationMs { get; }
        long PositionMs { get; }
        event Action Ended;
        void Close();
    }

    public interface IHost
    {
        void FrameReady();
        Glyph RenderGlyph(char c, int px, int style);
        void FontMetrics(int px, int style, out int height, out int ascent);
        int[] DecodeImage(byte[] data, out int w, out int h);
        IAudioClip CreateAudio(byte[] data, string mime);
        SoftSynth Midi { get; }
        void Vibrate(int ms);
        void RequestText(string title, string text, int maxSize, int constraints, Action<string> done);
        bool PlatformRequest(string url);
        void Log(string s);
        void Exited(string reason);
        string StorageDir { get; }
    }
}
