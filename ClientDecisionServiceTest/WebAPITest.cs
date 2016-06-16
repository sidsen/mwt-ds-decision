﻿using System;
using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System.Text;
using System.Globalization;

namespace ClientDecisionServiceTest
{
    public class MyContext
    {
        // Feature: Age:25
        public int Age { get; set; }

        // Feature: l:New_York
        [JsonProperty("l")]
        public string Location { get; set; }

        // Logged, but not used as feature due to leading underscore
        [JsonProperty("_isMember")]
        public bool IsMember { get; set; }

        // Not logged, not used as feature due to JsonIgnore
        [JsonIgnore]
        public string SessionId { get; set; }
    }

    [TestClass]
    public class WebApiTest
    {
        readonly string authToken = "mzf2xsxf4hjwe"; // insert auth token
        readonly string baseUrl = "http://dmdp1-webapi-jvj7wdftvsdwe.azurewebsites.net"; // insert API URL here
        readonly string contextType = "ranker";
        readonly int numActions = 3;

        [TestMethod]
        public void IndexExistsTest()
        {
            var wc = new WebClient();
            var indexUrl = baseUrl + "index.html";
            var response = wc.DownloadString(indexUrl);

            Assert.AreEqual("<!DOCTYPE html>", response.Substring(0,15));
        }

        [TestMethod]
        public void PostTest()
        {
            var wc = new WebClient();
            wc.Headers.Add("Authorization", authToken);

            string requestUri = string.Format(CultureInfo.InvariantCulture, "{0}/{1}", baseUrl, contextType);
            string payloadString = "{ Age: 25, Location: \"New York\", _multi: [{ a: 1}, { b: 2}]}";
            byte[] payload = System.Text.Encoding.ASCII.GetBytes(payloadString);
            var response = wc.UploadData(requestUri, "POST", payload);

            var utf8response = UnicodeEncoding.UTF8.GetString(response);

            // Compare only the decision, not the eventID
            Assert.AreEqual("{\"Action\":[1,2]", utf8response.Substring(0,15));
       }

        [TestMethod]
        [Ignore]
        public void ThroughputTest()
        {
            // stub

        }
    }
}