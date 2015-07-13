﻿using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ClientDecisionService;
using MultiWorldTesting;
using System.Net;
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Research.MultiWorldTesting.Contract;
using Newtonsoft.Json;
using System.Web;
using Microsoft.Research.DecisionService.Uploader;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System.Collections.Generic;

namespace ClientDecisionServiceTest
{
    [TestClass]
    public class UploaderTest
    {
        [TestMethod]
        public void TestUploaderSingleEvent()
        {
            joinServer.Reset();

            string uniqueKey = "test interaction";
            int eventSentCount = 0;

            var uploader = new EventUploader(null, MockJoinServer.MockJoinServerAddress);
            uploader.InitializeWithToken(MockCommandCenter.AuthorizationToken);
            uploader.PackageSent += (sender, e) => { eventSentCount += e.Records.Count(); };
            uploader.Upload(new SingleActionInteraction { Action = 1, Context = JsonConvert.SerializeObject(new TestContext()), Probability = 0.5, Key = uniqueKey });
            uploader.Flush();

            Assert.AreEqual(1, eventSentCount);
            Assert.AreEqual(1, joinServer.RequestCount);
            Assert.AreEqual(1, joinServer.EventBatchList.Count);
            Assert.AreEqual(1, joinServer.EventBatchList[0].ExperimentalUnitFragments.Count);
            Assert.AreEqual(uniqueKey, joinServer.EventBatchList[0].ExperimentalUnitFragments[0].Id);
            Assert.IsTrue(joinServer.EventBatchList[0].ExperimentalUnitFragments[0].Value.ToLower().Contains("\"a\":1,"));
        }

        [TestMethod]
        public void TestUploaderInvalidToken()
        {
            joinServer.Reset();

            string uniqueKey = "test interaction";
            int eventSentCount = 0;

            var uploader = new EventUploader(null, MockJoinServer.MockJoinServerAddress);
            bool exceptionCaught = false;

            uploader.InitializeWithToken("test");
            uploader.PackageSent += (sender, e) => { eventSentCount += e.Records.Count(); };
            uploader.PackageSendFailed += (sender, e) => { exceptionCaught = e.Exception != null; };

            uploader.Upload(new SingleActionInteraction { Action = 1, Context = JsonConvert.SerializeObject(new TestContext()), Probability = 0.5, Key = uniqueKey });
            uploader.Flush();

            Assert.AreEqual(1, joinServer.RequestCount);
            Assert.AreEqual(0, eventSentCount);
            Assert.AreEqual(0, joinServer.EventBatchList.Count);
            Assert.AreEqual(true, exceptionCaught);
        }

        [TestMethod]
        public void TestUploaderInvalidServerAddress()
        {
            joinServer.Reset();

            string uniqueKey = "test interaction";
            int eventSentCount = 0;

            var uploader = new EventUploader(null, "http://uploader.test");
            bool exceptionCaught = false;

            uploader.InitializeWithToken(MockCommandCenter.AuthorizationToken);
            uploader.PackageSent += (sender, e) => { eventSentCount += e.Records.Count(); };
            uploader.PackageSendFailed += (sender, e) => { exceptionCaught = e.Exception != null; };

            uploader.Upload(new SingleActionInteraction { Action = 1, Context = JsonConvert.SerializeObject(new TestContext()), Probability = 0.5, Key = uniqueKey });
            uploader.Flush();

            Assert.AreEqual(0, joinServer.RequestCount);
            Assert.AreEqual(0, eventSentCount);
            Assert.AreEqual(0, joinServer.EventBatchList.Count);
            Assert.AreEqual(true, exceptionCaught);
        }

        [TestMethod]
        public void TestUploaderInvalidConnectionString()
        {
            joinServer.Reset();

            string uniqueKey = "test interaction";
            int eventSentCount = 0;

            var uploader = new EventUploader(null, MockJoinServer.MockJoinServerAddress);
            bool exceptionCaught = false;

            uploader.InitializeWithConnectionString("testconnectionstring", 15);
            uploader.PackageSent += (sender, e) => { eventSentCount += e.Records.Count(); };
            uploader.PackageSendFailed += (sender, e) => { exceptionCaught = e.Exception != null; };

            uploader.Upload(new SingleActionInteraction { Action = 1, Context = JsonConvert.SerializeObject(new TestContext()), Probability = 0.5, Key = uniqueKey });
            uploader.Flush();

            Assert.AreEqual(1, joinServer.RequestCount);
            Assert.AreEqual(0, eventSentCount);
            Assert.AreEqual(0, joinServer.EventBatchList.Count);
            Assert.AreEqual(true, exceptionCaught);
        }

        [TestMethod]
        public void TestUploaderInvalidExperimentalUnitDuration()
        {
            joinServer.Reset();


            var uploader = new EventUploader(null, MockJoinServer.MockJoinServerAddress);
            bool exceptionCaught = false;

            try
            {
                uploader.InitializeWithConnectionString(MockCommandCenter.StorageConnectionString, -1);
            }
            catch (ArgumentException)
            {
                exceptionCaught = true;
            }

            Assert.AreEqual(true, exceptionCaught);
        }

        [TestMethod]
        public void TestUploaderMultipleEvents()
        {
            joinServer.Reset();

            string uniqueKey = "test interaction";
            int eventSentCount = 0;

            var uploader = new EventUploader(null, MockJoinServer.MockJoinServerAddress);
            uploader.InitializeWithToken(MockCommandCenter.AuthorizationToken);
            uploader.PackageSent += (sender, e) => { eventSentCount += e.Records.Count(); };

            uploader.Upload(new SingleActionInteraction { Action = 1, Context = JsonConvert.SerializeObject(new TestContext()), Probability = 0.5, Key = uniqueKey });
            uploader.Upload(new SingleActionInteraction { Action = 2, Context = JsonConvert.SerializeObject(new TestContext()), Probability = 0.7, Key = uniqueKey });
            uploader.Upload(new SingleActionInteraction { Action = 0, Context = JsonConvert.SerializeObject(new TestContext()), Probability = 0.5, Key = uniqueKey });

            uploader.Upload(new Observation { Value = "1", Key = uniqueKey });
            uploader.Upload(new Observation { Value = "2", Key = uniqueKey });
            uploader.Upload(new Observation { Value = JsonConvert.SerializeObject(new { value = "test outcome" }), Key = uniqueKey });

            uploader.Flush();

            Assert.AreEqual(6, eventSentCount);
            Assert.AreEqual(6, joinServer.EventBatchList.Sum(batch => batch.ExperimentalUnitFragments.Count));
        }

        [TestMethod]
        public void TestUploaderThreadSafeMock()
        {
            joinServer.Reset();

            string uniqueKey = "test interaction";
            int eventSentCount = 0;

            var uploader = new EventUploader(null, MockJoinServer.MockJoinServerAddress);
            uploader.InitializeWithToken(MockCommandCenter.AuthorizationToken);
            uploader.PackageSent += (sender, e) => { Interlocked.Add(ref eventSentCount, e.Records.Count()); };

            int numEvents = 1000;
            Parallel.For(0, numEvents, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 }, (i) =>
            {
                uploader.Upload(new SingleActionInteraction { Action = (uint)i, Context = JsonConvert.SerializeObject(new TestContext()), Probability = i / 1000.0, Key = uniqueKey });
                uploader.Upload(new Observation { Value = JsonConvert.SerializeObject(new { value = "999" + i }), Key = uniqueKey });
            });
            uploader.Flush();

            Assert.AreEqual(numEvents * 2, eventSentCount);
            Assert.AreEqual(numEvents * 2, joinServer.EventBatchList.Sum(batch => batch.ExperimentalUnitFragments.Count));
        }

        [TestInitialize]
        public void Setup()
        {
            joinServer = new MockJoinServer(MockJoinServer.MockJoinServerAddress);

            joinServer.Run();

            AzureStorageHelper.CleanCompleteBlobs();
        }

        [TestCleanup]
        public void CleanUp()
        {
            joinServer.Stop();
        }

        private MockJoinServer joinServer;
    }
}