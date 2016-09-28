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
using System.Timers;
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
            // Used to control expiration for fixed-duration events
            public DateTime ExpiresAt = DateTime.MaxValue;
        }

        // The experimental unit duration, or how long to wait for reward information before 
        // completing an event (TimeSpan.MaxValue means wait forever)
        private TimeSpan experimentalUnit;
        // Stores pending (incomplete) events, either for a fixed experimental unit duration or for 
        // manual completion
        private ConcurrentDictionary<string, DataPoint> pendingData;
        // A queue and timer to expire events based on the experimental unit duration
        private ConcurrentQueue<DataPoint> completionQueue;
        private Timer completionTimer;
        // Stores expired or manually-completed events 
        private ConcurrentDictionary<string, DataPoint> completeData = new ConcurrentDictionary<string, DataPoint>();
        // If no reward information is received, this value will be used
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
            pendingData = new ConcurrentDictionary<string,DataPoint>();
            // We only need the completion queue/timer if events are being completed automatically
            // (by experimental duration)
            if (experimentalUnit != TimeSpan.MaxValue)
            {
                completionQueue = new ConcurrentQueue<DataPoint>();
                completionTimer = new Timer(experimentalUnit.TotalMilliseconds);
                completionTimer.Elapsed += completeExpiredEvents;
                completionTimer.AutoReset = false;
                completionTimer.Start();
            }
        }

        private void completeExpiredEvents(object sender, ElapsedEventArgs e)
        {
            DataPoint dp;
            // At most one call to this handler can be in progress (and no one else dequeues
            // events), so we can safely dequeue
            while (completionQueue.TryPeek(out dp) && dp.ExpiresAt <= DateTime.Now)
            {
                if (completionQueue.TryDequeue(out dp))
                {
                    DataPoint dpActual;
                    bool keyLost = false;
                    // The key should exist unless the event was manually completed 
                    if (!pendingData.TryGetValue(dp.Key, out dpActual))
                    {
                        keyLost = true;
                    }
                    else
                    {
                        if (dp.Equals(dpActual))
                        {
                            // The removal must succeed, otherwise some corruption has occurred 
                            if (!pendingData.TryRemove(dp.Key, out dpActual))
                            {
                                keyLost = true;
                            }
                            else
                            {
                                completeData.AddOrUpdate(dp.Key, dpActual, (k, oldDp) => dpActual);
                            }
                        }
                        else
                        {
                            Trace.TraceWarning("Event with key {0} points to a new object, not completing", dp.Key);
                        }
                    }
                    if (keyLost)
                    {
                        Trace.TraceWarning("Event with key {0} missing (was it completed manually?)", dp.Key);
                    }
                }
            }

            // Reschedule the timer
            completionTimer.Interval = (dp != null) ? (dp.ExpiresAt - DateTime.Now).TotalMilliseconds : experimentalUnit.TotalMilliseconds;
            completionTimer.Start();
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
            if (experimentalUnit != TimeSpan.MaxValue)
            {
                dp.ExpiresAt = DateTime.Now.Add(experimentalUnit);
                // Add the datapoint to the dictionary of pending events 
                pendingData.AddOrUpdate(dp.Key, dp, (k, oldDp) => dp);
                // Also add it to the completion queue so it is expired at the right time
                completionQueue.Enqueue(dp);
                if (completionQueue.Count == 0)
                {
                    // We might overwrite a valid interval due to concurrency, but the worst that 
                    // happens is some events are completed a little later than they should (which
                    // is already possible due to tick resolution)
                    completionTimer.Interval = experimentalUnit.TotalMilliseconds;
                }
            }
            else
            {
                // Add the datapoint to the dictionary of pending events 
                pendingData.AddOrUpdate(dp.Key, dp, (k, oldDp) => dp);
            }
        }

        public void ReportReward(string uniqueKey, float reward)
        {
            DataPoint dp;
            if (pendingData.TryGetValue(uniqueKey, out dp))
            {
                // Guaranteed atomic by the language
                dp.Reward = reward;
            }
            else
            {
                Trace.TraceWarning("Could not find event with key {0}", uniqueKey);
            }
        }

        public void ReportRewardAndComplete(string uniqueKey, float reward)
        {
            DataPoint dp;
            // Attempt to remove and complete the event
            if (pendingData.TryRemove(uniqueKey, out dp))
            {
                // Guaranteed atomic by the language
                dp.Reward = reward;
                completeData.AddOrUpdate(dp.Key, dp, (k, oldDp) => dp);
            }
            else
            {
                Trace.TraceWarning("Could not find event with key {0}", uniqueKey);
            }
        }

        public void ReportOutcome(string uniqueKey, object outcome)
        {
            DataPoint dp;
            if (pendingData.TryGetValue(uniqueKey, out dp))
            {
                // Added to a concurrent bag, so thread-safe
                dp.Outcomes.Add(outcome);
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

    public class DecisionServiceLocal2<TContext> : DecisionServiceClient<TContext>
    {
        private VowpalWabbit<TContext> vw;
        // This serves as the base class's recorder/logger as well, but we keep a reference around
        // becauses it exposes additional APIs that aren't part of those interfaces (yet)
        private InMemoryLogger<TContext, int[]> log;

        public int ModelUpdateInterval;
        private int sinceLastUpdate = 0;

        // A snapshot of the current VW model
        public byte[] Model
        {
            get
            {
                MemoryStream currModel = new MemoryStream();
                vw.Native.SaveModel(currModel);
                return currModel.ToArray();
            }
        }

        public DecisionServiceLocal2(string vwArgs, int modelUpdateInterval, TimeSpan expUnit)
            : base(
            new DecisionServiceConfiguration("")
            {
                OfflineMode = true,
                OfflineApplicationID = Guid.NewGuid().ToString(),
                DevelopmentMode = false
            },
            new ApplicationClientMetadata
            {
                TrainArguments = vwArgs,
                InitialExplorationEpsilon = 1f
            },
            new VWExplorer<TContext>(null, JsonTypeInspector.Default, false))
        {
            this.log = new InMemoryLogger<TContext, int[]>(expUnit);
            this.Recorder = log;
            this.vw = new VowpalWabbit<TContext>(
                new VowpalWabbitSettings(vwArgs)
                {
                    TypeInspector = JsonTypeInspector.Default,
                    EnableStringExampleGeneration = true,
                    EnableStringFloatCompact = true
                }
                );
            this.ModelUpdateInterval = modelUpdateInterval;
        }

        /// <summary>
        /// Report a simple float reward for the experimental unit identified by the given unique key.
        /// </summary>
        /// <param name="reward">The simple float reward.</param>
        /// <param name="uniqueKey">The unique key of the experimental unit.</param>
        new public void ReportReward(float reward, string uniqueKey)
        {
            base.ReportReward(reward, uniqueKey);
            sinceLastUpdate++;
            updateModelMaybe();
        }

        public void ReportRewardAndComplete(float reward, string uniqueKey)
        {
            log.ReportRewardAndComplete(uniqueKey, reward);
            sinceLastUpdate++;
            updateModelMaybe();
        }

        private void updateModelMaybe()
        {
            if (sinceLastUpdate >= ModelUpdateInterval)
            {
                foreach (var dp in log.FlushCompleteEvents())
                {
                    uint action = (uint)((int[])dp.InteractData.Value)[0];
                    var label = new ContextualBanditLabel(action, -dp.Reward, ((GenericTopSlotExplorerState)dp.InteractData.ExplorerState).Probabilities[action - 1]);
                    vw.Learn((TContext)dp.InteractData.Context, label, index: (int)label.Action - 1);
                }
                MemoryStream currModel = new MemoryStream();
                vw.Native.SaveModel(currModel);
                currModel.Position = 0;
                this.UpdateModel(currModel);
                sinceLastUpdate = 0;
            }
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
