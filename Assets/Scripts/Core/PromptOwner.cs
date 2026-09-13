namespace VibeGame1
{
    /// <summary>
    /// Who is allowed to clear the standing prompt line (2026-09-06).
    ///
    /// <para>The line under the crosshair has one slot and many writers, and every one of them is
    /// EDGE-TRIGGERED — it raises only when its own string changes. Without an owner, a writer that went
    /// quiet ("" on the way out) blanked whatever another writer had standing there, and the blanked writer
    /// never re-raised because, to it, nothing had changed. The player lost a live "GRAPPLE  [DASH]" until
    /// they looked away and back.</para>
    ///
    /// <para>A key is just a string, so a new writer needs no change here — but adding its name keeps the
    /// list of everything that can speak in one readable place. <see cref="Anonymous"/> is the unowned
    /// write: it takes the line like any other, and anyone may clear it.</para>
    /// </summary>
    public static class PromptOwner
    {
        public const string Anonymous = "";
        /// <summary>ExecuteInteractor — "DEATHBLOW  [ATTACK]".</summary>
        public const string Execute = "execute";
        /// <summary>FlareGrapple — "GRAPPLE  [DASH]".</summary>
        public const string Grapple = "grapple";
        /// <summary>PlayerItems — the wall-surge countdown.</summary>
        public const string Surge = "surge";
        /// <summary>WandPedestal — the dev altar's "[F]" cue.</summary>
        public const string Pedestal = "pedestal";
        /// <summary>SandboxEnemySwitch — "[F]  WAKE …".</summary>
        public const string Sandbox = "sandbox";
        /// <summary>LevelEditor — the PLAYING banner.</summary>
        public const string LevelEditor = "leveleditor";
        /// <summary>DebugKeys — dev-key confirmations.</summary>
        public const string Debug = "debug";
        /// <summary>PlayerTimingCapture — the developer recording state.</summary>
        public const string TimingCapture = "timingcapture";
    }
}
