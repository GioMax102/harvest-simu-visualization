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
        public TraceData Trace { get; private set; }
        public TracePlayback Playback { get; private set; }
        public bool Paused { get; private set; } = true;
        public float Speed { get; private set; } = 8;
        double accumulated;
        int screenWidth, screenHeight;

        void Start()
        {
            if (hud) hud.Bind(this);
            LoadTrace(initialTrace);
        }
        public void LoadTrace(TextAsset asset)
        {
            Paused = true; accumulated = 0; Playback = null; Trace = null;
            if (worldView) worldView.Clear();
            try
            {
                if (!hud || !worldView) throw new InvalidOperationException("PlaybackController: asigna HUD y WorldView.");
                hud.Bind(this);
                if (!asset) throw new InvalidOperationException("Asigna un trace.json al campo Initial Trace del SimulationRoot.");
                var data = TraceLoader.Parse(asset.text);
                var playback = new TracePlayback(data);
                worldView.Build(data);
                Trace = data; Playback = playback;
                SetSpeed(initialSpeed);
                hud.ClearError(); RefreshView();
            }
            catch (Exception e)
            {
                Playback = null; Trace = null;
                if (worldView) worldView.Clear();
                string message = (asset ? asset.name : "trace") + ": " + e.Message;
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
        }
        void RefreshView()
        {
            if (Playback == null) return;
            worldView.Render(Playback.State);
            hud.Render(Trace, Playback.State, Paused, Speed);
        }
    }
}
