using System;
using UnityEngine;

namespace HarvestSim.Visualization
{
    public sealed class PlaybackController : MonoBehaviour
    {
        public TextAsset initialTrace;
        public WorldView worldView;
        public SimulationHUD hud;
        public float initialSpeed = 8;
        public string serverUrl = "http://127.0.0.1:8000";
        public TraceData Trace { get; private set; }
        public TracePlayback Playback { get; private set; }
        TracePlayback preview;
        public bool Paused { get; private set; } = true;
        public float Speed { get; private set; } = 8;
        double accumulated;
        int screenWidth, screenHeight;

        void Start()
        {
            if (hud) hud.Bind(this);
            LoadTrace(initialTrace);
            if (!TryGetComponent<TraceClient>(out _))
            {
                var client = gameObject.AddComponent<TraceClient>();
                client.controller = this; client.baseUrl = serverUrl;
            }
        }
        public void LoadTrace(TextAsset asset)
            => LoadTraceJson(asset ? asset.text : null, asset ? asset.name : "trace");

        public void LoadTraceJson(string json, string label = "trace")
        {
            Paused = true; accumulated = 0; Playback = null; preview = null; Trace = null;
            if (worldView) worldView.Clear();
            try
            {
                if (!hud || !worldView) throw new InvalidOperationException("PlaybackController: asigna HUD y WorldView.");
                hud.Bind(this);
                if (string.IsNullOrEmpty(json)) throw new InvalidOperationException("Asigna un trace.json al campo Initial Trace del SimulationRoot.");
                var data = TraceLoader.Parse(json);
                var playback = new TracePlayback(data);
                worldView.Build(data);
                Trace = data; Playback = playback; preview = new TracePlayback(data);
                SetSpeed(initialSpeed);
                hud.ClearError(); RefreshView();
            }
            catch (Exception e)
            {
                Playback = null; preview = null; Trace = null;
                if (worldView) worldView.Clear();
                string message = label + ": " + e.Message;
                if (hud) hud.ShowError(message); else Debug.LogError(message, this);
            }
        }
        public void PlayPause()
        {
            if (Playback == null) return;
            if (Playback.State.Tick < Trace.TotalTicks) Paused = !Paused;
            accumulated = 0; RefreshView();
        }
        public void StepForward() => MoveTo(Playback == null ? 0 : Playback.State.Tick + 1);
        public void StepBackward() => MoveTo(Playback == null ? 0 : Playback.State.Tick - 1);
        public void Restart() => MoveTo(0);
        public void GoToEnd() => MoveTo(Trace?.TotalTicks ?? 0);
        void MoveTo(int tick)
        {
            if (Playback == null) return;
            Paused = true; accumulated = 0; Playback.Seek(tick); RefreshView();
        }
        public void SetSpeed(float ticksPerSecond)
        {
            if (float.IsNaN(ticksPerSecond) || float.IsInfinity(ticksPerSecond) || ticksPerSecond <= 0)
                throw new ArgumentOutOfRangeException(nameof(ticksPerSecond), "Velocidad finita y positiva requerida.");
            Speed = ticksPerSecond; accumulated = 0;
            RefreshView();
        }
        void Update()
        {
            Advance(Time.unscaledDeltaTime);
            if (worldView && (Screen.width != screenWidth || Screen.height != screenHeight))
            {
                screenWidth = Screen.width; screenHeight = Screen.height;
                worldView.FrameCamera(worldView.mapCamera);
            }
        }
        // También permite comprobar el reloj sin depender de la duración de los frames del Test Runner.
        public void Advance(float seconds)
        {
            if (Paused || Playback == null) return;
            accumulated += seconds * (double)Speed;
            bool changed = false;
            while (accumulated >= 1 && Playback.State.Tick < Trace.TotalTicks)
            { accumulated--; Playback.Step(1); changed = true; }
            if (Playback.State.Tick == Trace.TotalTicks) { Paused = true; accumulated = 0; }
            if (changed) RefreshView();
            if (!Paused && Playback.State.Tick < Trace.TotalTicks)
            {
                preview.Seek(Playback.State.Tick + 1);
                worldView.Interpolate(Playback.State, preview.State, (float)accumulated);
            }
        }
        void RefreshView()
        {
            if (Playback == null) return;
            worldView.Render(Playback.State);
            hud.Render(Trace, Playback.State, Paused, Speed);
        }
    }
}
