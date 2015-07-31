﻿using ClientDecisionService;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MultiWorldTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ClientDecisionServiceTest
{
    [TestClass]
    public class ActionDependentFeaturesTest
    {
        [TestMethod]
        public void TestADFExplorationResult()
        {
            joinServer.Reset();

            var dsConfig = new DecisionServiceConfiguration<TestADFContext>(
                authorizationToken: MockCommandCenter.AuthorizationToken,
                explorer: new EpsilonGreedyExplorer<TestADFContext>(new TestADFPolicy(), epsilon: 0.5f))
            {
                PollingForModelPeriod = TimeSpan.MinValue,
                PollingForSettingsPeriod = TimeSpan.MinValue,
                LoggingServiceAddress = MockJoinServer.MockJoinServerAddress
            };

            var ds = new DecisionService<TestADFContext>(dsConfig);

            string uniqueKey = "eventid";

            for (int i = 1; i <= 100; i++)
            {
                uint[] action = ds.ChooseAction(uniqueKey, new TestADFContext(i));

                Assert.AreEqual(i, action.Length);

                // verify all unique actions in the list
                Assert.AreEqual(action.Length, action.Distinct().Count());

                // verify the actions are in the expected range
                Assert.AreEqual((i * (i + 1)) / 2, action.Sum(a => a));

                ds.ReportReward(i / 100f, uniqueKey);
            }

            ds.Flush();

            Assert.AreEqual(200, joinServer.EventBatchList.Sum(b => b.ExperimentalUnitFragments.Count));
        }

        [TestMethod]
        public void TestADFModelUpdateFromFile()
        {
            joinServer.Reset();

            var dsConfig = new DecisionServiceConfiguration<TestADFContextWithFeatures>(
                authorizationToken: MockCommandCenter.AuthorizationToken,
                explorer: new EpsilonGreedyExplorer<TestADFContextWithFeatures>(new TestADFWithFeaturesPolicy(), epsilon: 0.5f))
            {
                LoggingServiceAddress = MockJoinServer.MockJoinServerAddress,
                PollingForModelPeriod = TimeSpan.MinValue,
                PollingForSettingsPeriod = TimeSpan.MinValue
            };

            var ds = new DecisionService<TestADFContextWithFeatures>(dsConfig);

            string uniqueKey = "eventid";

            string modelFile = "test_vw_adf{0}.model";
            var actualModelFiles = new List<string>();

            for (int i = 1; i <= 100; i++)
            {
                Random rg = new Random(i);

                if (i % 50 == 1)
                {
                    int modelIndex = i / 50;
                    string currentModelFile = string.Format(modelFile, modelIndex);

                    byte[] modelContent = commandCenter.GetModelBlobContent(numExamples: 3 + modelIndex, numFeatureVectors: 4 + modelIndex);
                    System.IO.File.WriteAllBytes(currentModelFile, modelContent);

                    ds.UpdatePolicy(new VWPolicy<TestADFContextWithFeatures, TestADFFeatures>(currentModelFile));

                    actualModelFiles.Add(currentModelFile);
                }

                int numActions = rg.Next(5, 20);
                var context = TestADFContextWithFeatures.CreateRandom(numActions, rg);

                uint[] action = ds.ChooseAction(uniqueKey, context);

                Assert.AreEqual(numActions, action.Length);

                // verify all unique actions in the list
                Assert.AreEqual(action.Length, action.Distinct().Count());

                // verify the actions are in the expected range
                Assert.AreEqual((numActions * (numActions + 1)) / 2, action.Sum(a => a));

                ds.ReportReward(i / 100f, uniqueKey);
            }

            ds.Flush();

            Assert.AreEqual(200, joinServer.EventBatchList.Sum(b => b.ExperimentalUnitFragments.Count));

            foreach (string actualModelFile in actualModelFiles)
            {
                System.IO.File.Delete(actualModelFile);
            }
        }

        [TestMethod]
        public void TestADFModelUpdateFromStream()
        {
            joinServer.Reset();

            var dsConfig = new DecisionServiceConfiguration<TestADFContextWithFeatures>(
                authorizationToken: MockCommandCenter.AuthorizationToken,
                explorer: new EpsilonGreedyExplorer<TestADFContextWithFeatures>(new TestADFWithFeaturesPolicy(), epsilon: 0.5f))
            {
                LoggingServiceAddress = MockJoinServer.MockJoinServerAddress,
                PollingForModelPeriod = TimeSpan.MinValue,
                PollingForSettingsPeriod = TimeSpan.MinValue
            };

            var ds = new DecisionService<TestADFContextWithFeatures>(dsConfig);

            string uniqueKey = "eventid";

            string modelFile = "test_vw_adf{0}.model";

            for (int i = 1; i <= 100; i++)
            {
                Random rg = new Random(i);

                if (i % 50 == 1)
                {
                    int modelIndex = i / 50;
                    string currentModelFile = string.Format(modelFile, modelIndex);

                    byte[] modelContent = commandCenter.GetModelBlobContent(numExamples: 3 + modelIndex, numFeatureVectors: 4 + modelIndex);

                    var modelStream = new MemoryStream(modelContent);

                    ds.UpdatePolicy(new VWPolicy<TestADFContextWithFeatures, TestADFFeatures>(modelStream));
                }

                int numActions = rg.Next(5, 20);
                var context = TestADFContextWithFeatures.CreateRandom(numActions, rg);

                uint[] action = ds.ChooseAction(uniqueKey, context);

                Assert.AreEqual(numActions, action.Length);

                // verify all unique actions in the list
                Assert.AreEqual(action.Length, action.Distinct().Count());

                // verify the actions are in the expected range
                Assert.AreEqual((numActions * (numActions + 1)) / 2, action.Sum(a => a));

                ds.ReportReward(i / 100f, uniqueKey);
            }

            ds.Flush();

            Assert.AreEqual(200, joinServer.EventBatchList.Sum(b => b.ExperimentalUnitFragments.Count));
        }

        [TestInitialize]
        public void Setup()
        {
            joinServer = new MockJoinServer(MockJoinServer.MockJoinServerAddress);

            joinServer.Run();

            commandCenter = new MockCommandCenter(MockCommandCenter.AuthorizationToken);
        }

        [TestCleanup]
        public void CleanUp()
        {
            joinServer.Stop();
        }

        private MockJoinServer joinServer;
        private MockCommandCenter commandCenter;
    }
}