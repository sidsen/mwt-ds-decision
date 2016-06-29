using Microsoft.Research.MultiWorldTesting.ClientLibrary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VW.Serializer.Attributes;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace ClientDecisionServiceTest
{
    public class FoodContext2
    {
        [Feature]
        public string UserLocation { get; set; }

        public int[] Actions { get; set; }

        [JsonProperty(PropertyName = "_multi")]
        public FoodFeature2[] ActionDependentFeatures
        {
            get
            {
                return this.Actions
                    .Select((a, i) => new FoodFeature2(this.Actions.Length, i))
                    .ToArray();
            }
        }

        public static IReadOnlyCollection<FoodFeature2> GetFeaturesFromContext(FoodContext2 context)
        {
            return context.ActionDependentFeatures;
        }
    }

    public class FoodFeature2
    {
        public float[] Scores { get; set; }

        internal FoodFeature2(int numActions, int index)
        {
            Scores = Enumerable.Repeat(0f, numActions).ToArray();
            Scores[index] = index + 1;
        }
    }


    [TestClass]
    public class LocalModeTest
    {
        [TestMethod]
        public void TestDSLocalModelUpdate()
        {
            string vwArgs = "--cb_explore_adf --epsilon 0.2 --cb_type dr -q ::";
            DecisionServiceLocal<FoodContext> dsLocal = new DecisionServiceLocal<FoodContext>(vwArgs, 1, 10);
            
            // Generate random interactions and ensure the model changes at the right frequency.
            var context = new FoodContext { Actions = new int[] { 1, 2, 3 }, UserLocation = "HealthyTown" };
            var id = Guid.NewGuid().ToString();
            var prevModel = dsLocal.model.ToArray();
            dsLocal.ChooseAction(id, context, 1);
            dsLocal.ReportRewardAndComplete((float)1.0, id);
            Assert.IsTrue(!dsLocal.model.ToArray().SequenceEqual(prevModel));

            /*
            int numLocations = 2; // user location
            string[] locations = new string[] { "HealthyTown", "LessHealthyTown" };

            int numActions = 3; // food item
            int numExamplesPerActions = 10000;
            var recorder = new FoodRecorder();

            using (var vw = new VowpalWabbit<FoodContext>(
                new VowpalWabbitSettings("--cb_explore_adf --epsilon 0.2 --cb_type dr -q ::")
                {
                    TypeInspector = JsonTypeInspector.Default,
                    EnableStringExampleGeneration = true,
                    EnableStringFloatCompact = true
                }))
            {
                // Learn 
                var rand = new Random(0);
                for (int iE = 0; iE < numExamplesPerActions * numLocations; iE++)
                {
                    DateTime timeStamp = DateTime.UtcNow;

                    int iL = rand.Next(0, numLocations);

                    var context = new FoodContext { Actions = new int[] { 1, 2, 3 }, UserLocation = locations[iL] };
                    string key = "fooditem " + Guid.NewGuid().ToString();

                    int action = iE % numActions + 1;
                    recorder.Record(null, null, new EpsilonGreedyState { Probability = 1.0f / numActions }, null, key);

                    float cost = 0;

                    var draw = rand.NextDouble();
                    if (context.UserLocation == "HealthyTown")
                    {
                        // for healthy town, buy burger 1 with probability 0.1, burger 2 with probability 0.15, salad with probability 0.6
                        if ((action == 1 && draw < 0.1) || (action == 2 && draw < 0.15) || (action == 3 && draw < 0.6))
                        {
                            cost = -10;
                        }
                    }
                    else
                    {
                        // for unhealthy town, buy burger 1 with probability 0.4, burger 2 with probability 0.6, salad with probability 0.2
                        if ((action == 1 && draw < 0.4) || (action == 2 && draw < 0.6) || (action == 3 && draw < 0.2))
                        {
                            cost = -10;
                        }
                    }
                    var label = new ContextualBanditLabel
                    {
                        Action = (uint)action,
                        Cost = cost,
                        Probability = recorder.GetProb(key)
                    };
                    vw.Learn(context, label, index: (int)label.Action - 1);
                }
                var expectedActions = new Dictionary<string, uint>();
                expectedActions.Add("HealthyTown", 3);
                expectedActions.Add("LessHealthyTown", 2);
                for (int iE = 0; iE < numExamplesPerActions; iE++)
                {
                    foreach (string location in locations)
                    {
                        DateTime timeStamp = DateTime.UtcNow;

                        var context = new FoodContext { Actions = new int[] { 1, 2, 3 }, UserLocation = location };
                        ActionScore[] predicts = vw.Predict(context, VowpalWabbitPredictionType.ActionScore);
                        Assert.AreEqual(expectedActions[location], predicts[0].Action + 1);
                    }
                }
            }
             */
        }
    }
}

