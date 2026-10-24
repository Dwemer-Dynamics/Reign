using System;

namespace ReignBeta.World
{
    internal enum ReignSurrenderHandoffState { Waiting, AwaitingCapture, Captured, EncounterLost }

    // Victory is an intermediate state. Native results and the player's capture
    // choice must finish before the negotiated surrender can be fulfilled.
    internal sealed class ReignSurrenderHandoff
    {
        internal bool ResultsRequested { get; private set; }
        internal bool Finished { get; private set; }
        private ReignSurrenderHandoffState _terminal;

        internal ReignSurrenderHandoffState Advance(bool sameEncounter, bool interactionOpen,
            bool captiveInPlayerRoster, Action requestNativeResults)
        {
            if (Finished) return _terminal;
            if (captiveInPlayerRoster)
                return Finish(ReignSurrenderHandoffState.Captured);
            if (!sameEncounter)
                return Finish(ReignSurrenderHandoffState.EncounterLost);
            if (interactionOpen) return ReignSurrenderHandoffState.Waiting;
            if (!ResultsRequested)
            {
                // Set before calling native code: a throwing callback must never
                // cause results, loot or capture events to be applied twice.
                ResultsRequested = true;
                requestNativeResults();
            }
            return ReignSurrenderHandoffState.AwaitingCapture;
        }

        private ReignSurrenderHandoffState Finish(ReignSurrenderHandoffState state)
        {
            Finished = true;
            return _terminal = state;
        }
    }
}
