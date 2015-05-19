﻿//------------------------------------------------------------------------------
// <copyright>
//     Copyright (c) Microsoft Corporation. All Rights Reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace Microsoft.Research.MultiWorldTesting.Contract
{
    public static class ServiceConstants
    {
        public static readonly string JoinAddress = "http://decisionservice.cloudapp.net";
        public static readonly string JoinPostAddress = "/join";
        public static readonly string TokenAuthenticationScheme = "Bearer";
        public static readonly string ConnectionStringAuthenticationScheme = "AzureStorage";

        public static readonly string IncompleteContainerPrefix = "incomplete";
        public static readonly string JoinedBlobContainerPrefix = "complete";
    }
}