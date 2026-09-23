using UnityEngine;
using UnityEngine.Serialization;

namespace CardGame.ActionQueue
{
    public sealed partial class ActionQueueRunner : ISerializationCallbackReceiver
    {
        #region Logging Configuration

        [SerializeField]
        private ActionQueueLogLevel logLevel = ActionQueueLogLevel.WarningsAndErrors;

        [SerializeField, HideInInspector, FormerlySerializedAs("verboseLogging")]
        private bool legacyVerboseLogging;

        [SerializeField, HideInInspector]
        private bool logLevelMigrated;

        public ActionQueueLogLevel LogLevel
        {
            get => logLevel;
            set
            {
                logLevel = value;
                if (_engine != null)
                    _engine.LogLevel = value;
            }
        }

        private ActionQueueOptions CreateEngineOptions()
        {
            return new ActionQueueOptions
            {
                MaxActionsPerChain = maxActionsPerChain,
                TraceCapacity = traceCapacity,
                LogLevel = logLevel,
                SkipPresentationWaits = skipPresentationWaits
            };
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            if (logLevelMigrated)
                return;

            logLevel = legacyVerboseLogging
                ? ActionQueueLogLevel.Verbose
                : ActionQueueLogLevel.WarningsAndErrors;
            logLevelMigrated = true;
        }

        #endregion
    }
}
