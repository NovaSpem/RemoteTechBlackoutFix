using System;
using System.Reflection;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using CommNet;
using RemoteTech;

namespace RemoteTechBlackoutFix
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class RTPlasmaBlackoutMonitor : MonoBehaviour
    {
        private double nextCheckTime = 0;
        private const double CHECK_INTERVAL = 0.2;

        private bool isEffectActive = false;
        private float lastSentIntensity = 0f;
        
        private bool wasSignalLostInPlasma = false;
        private bool plasmaPiercedMessageShown = false;
        private bool forceEnable = false;
        private bool plasmaBlackoutEnabled = true;
        private bool showMessages = true;
        private bool debugLog = false;

        private static bool isFireflyInstalled = false;
        private static Type fireflyModuleType = null;
        private static MethodInfo getEntryStrengthMethod = null;
        private static bool fireflyDetectionAttempted = false;

        private float plasmaMinValue = 350f;
        private float plasmaMaxValue = 2000f;

        private static Dictionary<string, float> factoryRangeCache = new Dictionary<string, float>();

        void Start()
        {
            LoadConfiguration();
            DetectFireflyMod();
            
            if (!forceEnable && HighLogic.CurrentGame != null)
            {
                var commNetParams = HighLogic.CurrentGame.Parameters.CustomParams<CommNetParams>();
                if (commNetParams != null)
                {
                    plasmaBlackoutEnabled = commNetParams.plasmaBlackout;
                }
            }
        }

        private void LoadConfiguration()
        {
            try
            {
                if (GameDatabase.Instance == null) return;
                
                ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes("RT_PLASMA_SETTINGS");
                if (nodes != null && nodes.Length > 0)
                {
                    ConfigNode config = nodes[0];
                    
                    if (config.HasValue("forceEnable")) 
                    {
                        bool.TryParse(config.GetValue("forceEnable"), out forceEnable);
                    }
                    if (config.HasValue("plasmaMinValue"))
                    {
                        float.TryParse(config.GetValue("plasmaMinValue"), out plasmaMinValue);
                    }
                    if (config.HasValue("plasmaMaxValue"))
                    {
                        float.TryParse(config.GetValue("plasmaMaxValue"), out plasmaMaxValue);
                    }
                    if (config.HasValue("showMessages"))
                    {
                        bool.TryParse(config.GetValue("showMessages"), out showMessages);
                    }
                    if (config.HasValue("debugLog"))
                    {
                        bool.TryParse(config.GetValue("debugLog"), out debugLog);
                    }
                    
                    UnityEngine.Debug.Log(string.Format("[RTPlasmaFix] Конфиг загружен. forceEnable = {0}, plasmaMin = {1}, plasmaMax = {2}, showMessages = {3}, debugLog = {4}", 
                        forceEnable, plasmaMinValue, plasmaMaxValue, showMessages, debugLog));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError(string.Format("[RTPlasmaFix] Ошибка загрузки конфига: {0}", ex.Message));
            }
        }

        private void DetectFireflyMod()
        {
            if (fireflyDetectionAttempted) return;
            fireflyDetectionAttempted = true;
            
            var loadedAssy = AssemblyLoader.loadedAssemblies.FirstOrDefault(
                a => a.name.Equals("Firefly", StringComparison.OrdinalIgnoreCase));
            
            if (loadedAssy != null && loadedAssy.assembly != null)
            {
                isFireflyInstalled = true;
                fireflyModuleType = loadedAssy.assembly.GetType("Firefly.AtmoFxModule");
                
                if (fireflyModuleType != null)
                {
                    getEntryStrengthMethod = fireflyModuleType.GetMethod(
                        "GetEntryStrength", 
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                }
            }

            if (isFireflyInstalled && getEntryStrengthMethod != null)
                UnityEngine.Debug.Log(string.Format("[RTPlasmaFix] Addon successfully launched! Firefly range configured: [{0} - {1}].", plasmaMinValue, plasmaMaxValue));
            else
                UnityEngine.Debug.LogWarning("[RTPlasmaFix] Firefly graphics mod not found.");
        }

        void FixedUpdate()
        {
            double currentTime = Planetarium.GetUniversalTime();
            if (currentTime < nextCheckTime) return;
            nextCheckTime = currentTime + CHECK_INTERVAL;

            if (!forceEnable && !plasmaBlackoutEnabled)
            {
                if (isEffectActive) ForceRestoreAll();
                return; 
            }

            Vessel activeVessel = FlightGlobals.ActiveVessel;
            if (activeVessel == null || activeVessel.vesselModules == null) return;

            float rawFireflyStrength = GetFireflyPlasmaStrength(activeVessel);
            float plasmaIntensity = Mathf.InverseLerp(plasmaMinValue, plasmaMaxValue, rawFireflyStrength);

            if (debugLog)
            {
                UnityEngine.Debug.Log(string.Format("[RTPlasmaFix] Vessel: {0} | Firefly's value: {1:F1} | Signal: {2:P0}", 
                    activeVessel.vesselName, rawFireflyStrength, plasmaIntensity));
            }

            if (plasmaIntensity > 0.01f)
            {
                float antennaEfficiency = Mathf.Lerp(1.0f, 0.0f, plasmaIntensity);
                ApplyDynamicPlasma(activeVessel, antennaEfficiency);
                CheckConnectionStatus();
            }
            else if (isEffectActive)
            {
                ForceRestoreAll();
            }
        }

        private float GetFireflyPlasmaStrength(Vessel vessel)
        {
            if (!isFireflyInstalled || fireflyModuleType == null || getEntryStrengthMethod == null)
                return 0f;

            try
            {
                object fireflyModule = null;
                for (int i = 0; i < vessel.vesselModules.Count; i++)
                {
                    var mod = vessel.vesselModules[i];
                    if (mod != null && fireflyModuleType.IsAssignableFrom(mod.GetType()))
                    {
                        fireflyModule = mod;
                        break;
                    }
                }

                if (fireflyModule != null)
                {
                    object result = getEntryStrengthMethod.Invoke(fireflyModule, null);
                    if (result is float)
                        return (float)result;
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning(string.Format("[RTPlasmaFix] Error retrieving Firefly data: {0}", ex.Message));
            }

            return 0f;
        }

        private void CheckConnectionStatus()
        {
            if (!isEffectActive) return;

            try
            {
                Vessel activeVessel = FlightGlobals.ActiveVessel;
                if (activeVessel == null) return;

                bool hasNetworkConnection = RemoteTech.API.API.HasAnyConnection(activeVessel.id);

                if (!hasNetworkConnection)
                {
                    wasSignalLostInPlasma = true;
                    plasmaPiercedMessageShown = false; 
                }
                else if (wasSignalLostInPlasma && !plasmaPiercedMessageShown)
                {
                    plasmaPiercedMessageShown = true;
                    wasSignalLostInPlasma = false;
                    if (showMessages)
                    {
                        ScreenMessages.PostScreenMessage(
                            "[RTPlasmaFix] Connection partially restored", 
                            4.0f, 
                            ScreenMessageStyle.UPPER_CENTER);
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError(string.Format("[RTPlasmaFix] Error RemoteTech API: {0}", ex.Message));
            }
        }

        private void ApplyDynamicPlasma(Vessel vessel, float efficiency)
        {
            if (efficiency >= 0.99f)
            {
                if (isEffectActive) ForceRestoreAll();
                return;
            }

            if (isEffectActive && Mathf.Abs(efficiency - lastSentIntensity) < 0.01f) return;

            bool foundAntenna = ProcessAntennas(vessel, efficiency);

            if (foundAntenna && !isEffectActive)
            {
                isEffectActive = true;
                wasSignalLostInPlasma = false; 
                plasmaPiercedMessageShown = false;
                if (showMessages)
                {
                    ScreenMessages.PostScreenMessage(
                        "[RTPlasmaFix] Plasma blocks the signal!", 
                        3.0f, 
                        ScreenMessageStyle.UPPER_CENTER);
                }
            }
            lastSentIntensity = efficiency;
        }

        private bool ProcessAntennas(Vessel vessel, float efficiency)
        {
            bool foundAntenna = false;

            for (int i = 0; i < vessel.parts.Count; i++)
            {
                Part part = vessel.parts[i];
                if (part == null || part.Modules == null) continue;

                for (int j = 0; j < part.Modules.Count; j++)
                {
                    PartModule module = part.Modules[j];
                    if (module != null && module.GetType().Name == "ModuleRTAntenna")
                    {
                        var type = module.GetType();
                        
                        var omniField = type.GetField("Mode1OmniRange");
                        if (omniField != null)
                        {
                            float factoryOmni = GetFactoryRange(part, "Mode1OmniRange");
                            if (factoryOmni > 0f)
                            {
                                foundAntenna = true;
                                omniField.SetValue(module, factoryOmni * efficiency);
                            }
                        }

                        var dishField = type.GetField("Mode1DishRange");
                        if (dishField != null)
                        {
                            float factoryDish = GetFactoryRange(part, "Mode1DishRange");
                            if (factoryDish > 0f)
                            {
                                foundAntenna = true;
                                dishField.SetValue(module, factoryDish * efficiency);
                            }
                        }

                        var updateMethod = type.GetMethod("UpdateAndReturnConnectivity");
                        if (updateMethod != null) updateMethod.Invoke(module, null);
                    }
                }
            }

            return foundAntenna;
        }

        private void ForceRestoreAll()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || vessel.parts == null)
            {
                ResetState();
                return;
            }

            ProcessAntennas(vessel, 1.0f);
            
            ResetState();
            if (showMessages)
            {
                ScreenMessages.PostScreenMessage(
                    "[RTPlasmaFix] The signal has been fully restored.", 
                    3.0f, 
                    ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private void ResetState()
        {
            isEffectActive = false;
            lastSentIntensity = 0f;
            wasSignalLostInPlasma = false;
            plasmaPiercedMessageShown = false;
        }

        private float GetFactoryRange(Part part, string fieldName)
        {
            if (part == null || part.partInfo == null || part.partInfo.partPrefab == null || 
                part.partInfo.partPrefab.Modules == null)
                return -1f;

            string cacheKey = string.Format("{0}_{1}", part.partInfo.name, fieldName);
            
            float cachedValue;
            if (factoryRangeCache.TryGetValue(cacheKey, out cachedValue))
                return cachedValue;

            Part factoryPart = part.partInfo.partPrefab;
            for (int i = 0; i < factoryPart.Modules.Count; i++)
            {
                PartModule module = factoryPart.Modules[i];
                if (module != null && module.GetType().Name == "ModuleRTAntenna")
                {
                    var field = module.GetType().GetField(fieldName);
                    if (field != null)
                    {
                        float value = (float)field.GetValue(module);
                        factoryRangeCache[cacheKey] = value;
                        return value;
                    }
                }
            }
            return -1f;
        }
    }
}