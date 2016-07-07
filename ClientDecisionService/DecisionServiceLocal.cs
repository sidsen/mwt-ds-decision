using Microsoft.Research.MultiWorldTesting.Contract;
using Microsoft.Research.MultiWorldTesting.JoinUploader;
using Microsoft.Research.MultiWorldTesting.ExploreLibrary;
using VW;
using VW.Labels;
using VW.Serializer;
using System;
using System.IO;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.Caching;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Microsoft.Research.MultiWorldTesting.ClientLibrary
{
    /// <summary>
    /// Joins interactions with rewards locally, using an in-memory cache. The public API of this
    /// logger is thread-safe.
    /// </summary>
    /// <typeparam name="TContext">The Context type</typeparam>
    /// <typeparam name="TAction">The Action type</typeparam>
    internal class InMemoryLogger<TContext, TAction> : IRecorder<TContext, TAction>, ILogger
    {
        /// <summary>
        /// An exploration datapoint, containing the context, action, probability, and reward
        /// of a given event. In other words, the <x,a,r,p> tuple.
        /// </summary>
        internal class DataPoint : IEvent
        {
            public string Key { get; set; }
            // Contains the context, action, and probability (at least)
            public Interaction InteractData { get; set; }
            public float Reward { get; set; }
            // TODO: This can be used to support custom reward functions
            public ConcurrentBag<object> Outcomes = new ConcurrentBag<object>();
        }

        /// <summary>
        /// The experimental unit duration, or how long to wait for reward information (set to
        /// TimeSpan.MaxValue for infinite duration
        /// </summary>
        public TimeSpan experimentalUnit;

        // Handles pending (incomplete) events with a fixed experimental unit duration
        private MemoryCache pendingData;
        // Stores events that have expired or were completed manually
        private ConcurrentDictionary<string, DataPoint> completeData = new ConcurrentDictionary<string, DataPoint>();
        private float defaultReward;

        /// <summary>
        /// Creates a new in-memory logger for exploration data
        /// </summary>
        /// <param name="expUnit">The experimental unit duration, or how long to wait for reward 
        /// information. Set this to TimeSpan.MaxValue for infinite duration (events never expire
        /// and must be completed manually.</param>
        /// <param name="defaultReward">Reward value to use when no reward signal is received</param>
        public InMemoryLogger(TimeSpan expUnit, float defaultReward = (float)0.0)
        {
            this.experimentalUnit = expUnit;
            this.defaultReward = defaultReward;
            pendingData = new MemoryCache(Guid.NewGuid().ToString());
        }

        public void Record(TContext context, TAction value, object explorerState, object mapperState, string uniqueKey)
        {
            DataPoint dp = new DataPoint
            {
                Key = uniqueKey,
                InteractData = new Interaction
                {
                    Key = uniqueKey,
                    Context = context,
                    Value = value,
                    ExplorerState = explorerState,
                    MapperState = mapperState
                },
                Reward = defaultReward
            };
            CacheItemPolicy policy = new CacheItemPolicy();
            if (experimentalUnit != TimeSpan.MaxValue)
            {
                policy.AbsoluteExpiration = new DateTimeOffset(DateTime.Now.Add(experimentalUnit));
            }
            policy.RemovedCallback = EventRemovedCallback;
            // If the key exists silently update the data
            pendingData.Set(uniqueKey, dp, policy);
        }

        public void ReportReward(string uniqueKey, float reward)
        {
            DataPoint dp = (DataPoint)pendingData.Get(uniqueKey);
            if (dp != null)
            {
                // Guaranteed atomic by the language
                dp.Reward = reward;
            }
            else
            {
                Trace.TraceWarning("Could not find interaction data corresponding to reward for key {0}", uniqueKey);
            }
        }

        public void ReportRewardAndComplete(string uniqueKey, float reward)
        {
            DataPoint dp = (DataPoint)pendingData.Get(uniqueKey);
            if (dp != null)
            {
                // Guaranteed atomic by the language
                dp.Reward = reward;
                // Complete the event by evicting it from the cache (this should call the removed
                // callback)
                pendingData.Remove(uniqueKey);
            }
            else
            {
                Trace.TraceWarning("Could not find interaction data corresponding to reward for key {0}", uniqueKey);
            }
        }

        public void ReportOutcome(string uniqueKey, object outcome)
        {
            DataPoint dp = (DataPoint)pendingData.Get(uniqueKey);
            if (dp != null)
            {
                // Added to a concurrent bag, so thread safe
                dp.Outcomes.Add(outcome);
            }
            else
            {
                Trace.TraceWarning("Could not find interaction data corresponding to outcome for key {0}", uniqueKey);
            }
        }

        private void EventRemovedCallback(CacheEntryRemovedArguments args)
        {
            if (args.RemovedReason == CacheEntryRemovedReason.Expired ||
                args.RemovedReason == CacheEntryRemovedReason.Removed)
            {
                DataPoint dp = (DataPoint)args.CacheItem.Value;
                // Note: this silently updates the data if the key exists
                completeData.AddOrUpdate(args.CacheItem.Key, dp, (k, oldDp) => dp);
            }
            else if (args.RemovedReason == CacheEntryRemovedReason.Evicted)
            {
                Trace.TraceError("Interaction data evicted from cache due to lack of memory (may result in biased exploration dataset)!");
            }
            else
            {
                throw new Exception("Interaction data evicted from cache due to unknown reason");
            }
        }

        public DataPoint[] FlushCompleteEvents()
        {
            DataPoint temp;
            // Get a snapshot of the complete events, then iterate through and try to remove each
            // one, returning only the successfully removed ones. This ensures each data point is
            // returned at most once.
            var datapoints = completeData.ToArray();
            List<DataPoint> removed = new List<DataPoint>();
            foreach (var dp in datapoints)
            {
                if (completeData.TryRemove(dp.Key, out temp))
                {
                    removed.Add(temp);
                }
            }
            return removed.ToArray();
        }
    }

    public class DecisionServiceLocal<TContext>
    {
        public DecisionServiceClient<TContext> dsClient;
        private VowpalWabbit<TContext> vw;
        private InMemoryLogger<TContext, int[]> log;
        public MemoryStream model;

        private int modelUpdateInterval;
        private int sinceLastUpdate = 0;

        public DecisionServiceLocal(string vwArgs, int modelUpdateInterval, TimeSpan expUnit)
        {
            var config = new DecisionServiceConfiguration("") 
            { 
                OfflineMode = true, 
                OfflineApplicationID = Guid.NewGuid().ToString(),
                DevelopmentMode = false
            };
            var metaData = new ApplicationClientMetadata
            {
                TrainArguments = vwArgs,
                InitialExplorationEpsilon = 1f
            };

            dsClient = DecisionService.Create<TContext>(config, JsonTypeInspector.Default, metaData);
            log = new InMemoryLogger<TContext, int[]>(expUnit);
            dsClient.Recorder = log;
            vw = new VowpalWabbit<TContext>(
                new VowpalWabbitSettings(vwArgs)
                {
                    TypeInspector = JsonTypeInspector.Default,
                    EnableStringExampleGeneration = true,
                    EnableStringFloatCompact = true
                }
                );
            this.modelUpdateInterval = modelUpdateInterval;
            model = new MemoryStream();
        }

        public int ChooseAction(string uniqueKey, TContext context, int defaultAction)
        {
            return dsClient.ChooseAction(uniqueKey, context, defaultAction);
        }

        /// <summary>
        /// Report a simple float reward for the experimental unit identified by the given unique key.
        /// </summary>
        /// <param name="reward">The simple float reward.</param>
        /// <param name="uniqueKey">The unique key of the experimental unit.</param>
        public void ReportReward(float reward, string uniqueKey)
        {
            throw new Exception("Don't call this, Josh");
            dsClient.ReportReward(reward, uniqueKey);
            sinceLastUpdate++;
            if (sinceLastUpdate == modelUpdateInterval)
            {
                foreach (var dp in log.FlushCompleteEvents())
                {
                    uint action = (uint)((int[])dp.InteractData.Value)[0];
                    var label = new ContextualBanditLabel(action, dp.Reward, ((GenericTopSlotExplorerState)dp.InteractData.ExplorerState).Probabilities[action-1]);
                    Console.WriteLine(label);
                    vw.Learn((TContext)dp.InteractData.Context, label, index: (int)label.Action - 1);
                    //vw.Learn(new[] { "1:-3:0.2 | b:2"});
                    model = new MemoryStream();
                    vw.Native.SaveModel(model);
                    dsClient.UpdateModel(model);
                }
                sinceLastUpdate = 0;
            }
        }

        public void ReportRewardAndComplete(float reward, string uniqueKey)
        {
            //TODO: CHANGE THIS UGLY CAST
            (dsClient.Recorder as InMemoryLogger<TContext, int[]>).ReportRewardAndComplete(uniqueKey, reward);
            sinceLastUpdate++;
            if (sinceLastUpdate == modelUpdateInterval)
            {
                foreach (var dp in log.FlushCompleteEvents())
                {
                    uint action = (uint)((int[])dp.InteractData.Value)[0];
                    var label = new ContextualBanditLabel(action, dp.Reward, ((GenericTopSlotExplorerState)dp.InteractData.ExplorerState).Probabilities[action-1]);
                    vw.Learn((TContext)dp.InteractData.Context, label, index: (int)label.Action - 1);
                }
                model = new MemoryStream();
                vw.Native.SaveModel(model);
                model.Position = 0;
                dsClient.UpdateModel(model);
                sinceLastUpdate = 0;
            }
        }
    }
}
