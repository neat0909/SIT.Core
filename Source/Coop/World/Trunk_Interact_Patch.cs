﻿using EFT;
using EFT.Interactive;
using SIT.Core.Core;
using SIT.Core.Misc;
using SIT.Tarkov.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SIT.Core.Coop.World
{
    internal class Trunk_Interact_Patch : ModulePatch
    {
        public static Type InstanceType => typeof(Trunk);

        public static string MethodName => "Trunk_Interact";

        protected override MethodBase GetTargetMethod()
        {
            return ReflectionHelpers.GetAllMethodsForType(InstanceType).FirstOrDefault(x => x.Name == "Interact" && x.GetParameters().Length == 1 && x.GetParameters()[0].Name == "interactionResult");
        }

        static ConcurrentBag<long> ProcessedCalls = new();

        protected static bool HasProcessed(Dictionary<string, object> dict)
        {
            var timestamp = long.Parse(dict["t"].ToString());

            if (!ProcessedCalls.Contains(timestamp))
            {
                ProcessedCalls.Add(timestamp);
                return false;
            }

            return true;
        }

        [PatchPrefix]
        public static bool Prefix(Trunk __instance)
        {
            return false;
        }

        [PatchPostfix]
        public static void Postfix(Trunk __instance, InteractionResult interactionResult)
        {
            Dictionary<string, object> packet = new()
            {
                { "t", DateTime.Now.Ticks.ToString("G") },
                { "serverId", CoopGameComponent.GetServerId() },
                { "m", MethodName },
                { "trunkId", __instance.Id },
                { "type", interactionResult.InteractionType.ToString() }
            };

            if (__instance.InteractingPlayer != null)
                packet.Add("player", __instance.InteractingPlayer.ProfileId);

            AkiBackendCommunication.Instance.PostDownWebSocketImmediately(packet);
        }

        public static void Replicated(Dictionary<string, object> packet)
        {
            if (HasProcessed(packet))
                return;

            if (Enum.TryParse(packet["type"].ToString(), out EInteractionType interactionType))
            {
                CoopGameComponent coopGameComponent = CoopGameComponent.GetCoopGameComponent();
                Trunk trunk = coopGameComponent.ListOfInteractiveObjects.FirstOrDefault(x => x.Id == packet["trunkId"].ToString()) as Trunk;

                if (trunk != null)
                {
                    string methodName = string.Empty;
                    switch (interactionType)
                    {
                        case EInteractionType.Open:
                            methodName = "Open";
                            break;
                        case EInteractionType.Close:
                            methodName = "Close";
                            break;
                        case EInteractionType.Unlock:
                            methodName = "Unlock";
                            break;
                        case EInteractionType.Breach:
                            break;
                        case EInteractionType.Lock:
                            methodName = "Lock";
                            break;
                    }

                    EFT.Player player = null;
                    if (packet.ContainsKey("player"))
                    {
                        player = Comfort.Common.Singleton<GameWorld>.Instance.GetAlivePlayerByProfileID(packet["player"].ToString());
                        if (player != null)
                        {
                            if (!AkiBackendCommunication.Instance.HighPingMode && !player.IsYourPlayer)
                            {
                                if (SIT.Coop.Core.Matchmaker.MatchmakerAcceptPatches.IsClient || coopGameComponent.PlayerUsers.Contains(player))
                                {
                                    WorldInteractiveObject.InteractionParameters interactionParameters = trunk.GetInteractionParameters(player.Transform.position);
                                    player.SendHandsInteractionStateChanged(true, interactionParameters.AnimationId);
                                    player.HandsController.Interact(true, interactionParameters.AnimationId);
                                }
                            }
                        }
                    }

                    trunk.StartBehaviourTimer(EFTHardSettings.Instance.DelayToOpenContainer, () => ReflectionHelpers.InvokeMethodForObject(trunk, methodName));
                }
                else
                {
                    Logger.LogDebug("Trunk_Interact_Patch:Replicated: Couldn't find Switch in at all in world?");
                }
            }
            else
            {
                Logger.LogError("Trunk_Interact_Patch:Replicated:EInteractionType did not parse correctly!");
            }
        }
    }
}