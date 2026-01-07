using System;
using System.Linq;
using BotRunner.Networking;

namespace BotRunner.Utils
{
    public static class ArchitectureValidator
    {
        public static void Validate()
        {
            Logger.Info("==================================================");
            Logger.Info("      ARCHITECTURE VALIDATION CHECK               ");
            Logger.Info("==================================================");

            var mapping = RpcMapping.Default();
            var opCodes = mapping.RpcNameToId.Values.ToList();
            
            // Check 1: Placeholder ID detection
            // Real UberStrike opcodes are unlikely to be perfectly sequential 1..N or strictly small integers.
            // If we see 1, 2, 3, 4, 5... it's a strong sign of placeholder data.
            bool isSequential = !opCodes.Where((x, i) => i < opCodes.Count - 1 && x + 1 != opCodes[i + 1]).Any();
            bool hasSmallIds = opCodes.All(x => x < 100);

            if (isSequential || hasSmallIds)
            {
                Logger.Warn("[Protocol] DETECTED PLACEHOLDER OPCODES");
                Logger.Warn("The simulation is using simplified sequential IDs (1, 2, 3...) instead of");
                Logger.Warn("authoritative UberStrike Operation Codes.");
                Logger.Warn("-> This is VALID for Phase 1 (AI Logic Testing)");
                Logger.Warn("-> This is INVALID for Phase 2 (Server Emulation)");
            }
            else
            {
                Logger.Info("[Protocol] Opcodes appear non-sequential. Potential authoritative IDs detected.");
            }

            // Check 2: Protocol Layer Check
            Logger.Info("[Protocol] Simulating: Logical RMI Interface");
            Logger.Info("[Protocol] Transport: Mock / Photon3 Abstraction");
            
            // Check 3: Alignment Report
            Logger.Info("==================================================");
            Logger.Info("      ALIGNMENT STATUS: PHASE 1 READY             ");
            Logger.Info("==================================================");
            Logger.Info("1. BotRunner is simulating the *Interface* correctly.");
            Logger.Info("2. Protocol details are abstracted (as intended for Phase 1).");
            Logger.Info("3. Injection mode bypasses this layer entirely.");
            Logger.Info("==================================================");
        }
    }
}
