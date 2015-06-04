﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Serialization;

namespace Microsoft.Research.DecisionService.Uploader
{
    /// <summary>
    /// A JSON.NET reference resolver keeping maintaining an age and/or size based cache of references.
    /// Reference ids are GUIDs to be valid across multiple requests.
    /// </summary>
    /// <remarks>
    /// Only supports serialization.
    /// </remarks>
    public sealed class CachingReferenceResolver : IReferenceResolver
    {
        private readonly object objectLock = new object();
        private readonly Dictionary<object, CacheItem> references;
        private readonly int maxCapacity;
        private readonly TimeSpan maxAge;

        /// <summary>
        /// Constructs a caching reference resolver with no capacity bound, no age limit and uses reference equality.
        /// </summary>
        public CachingReferenceResolver()
            : this(TimeSpan.MaxValue, int.MaxValue, null)
        {
        }

        /// <summary>
        /// Constructs a caching reference resolver with no age limit and uses reference equality.
        /// </summary>
        /// <param name="maxCapacity">The maximum number of references to cache. If the limit is reached, the oldest references are evicted.</param>
        public CachingReferenceResolver(int maxCapacity)
            : this(TimeSpan.MaxValue, maxCapacity, null)
        {
        }

        /// <summary>
        /// Constructs a caching reference resolver with no capacity bound and no age limit.
        /// </summary>
        /// <param name="equalityComparer">An equality comparer to be used to determine equality of referenced objects.</param>
        public CachingReferenceResolver(IEqualityComparer<object> equalityComparer)
            : this(TimeSpan.MaxValue, int.MaxValue, equalityComparer)
        {
        }

        /// <summary>
        /// Constructs a caching reference resolver with no capacity bound and uses reference equality.
        /// </summary>
        /// <param name="maxAge">The time a reference should be cached.</param>
        public CachingReferenceResolver(TimeSpan maxAge)
            : this(maxAge, int.MaxValue, null)
        {
        }

        /// <summary>
        /// Constructs a caching reference resolver.
        /// </summary>
        /// <param name="maxAge">The time a reference should be cached.</param>
        /// <param name="maxCapacity">The maximum number of references to cache. If the limit is reached, the oldest references are evicted.</param>
        /// <param name="equalityComparer">An equality comparer to be used to determine equality of referenced objects.</param>
        public CachingReferenceResolver(TimeSpan maxAge, int maxCapacity, IEqualityComparer<object> equalityComparer)
        {
            if (equalityComparer == null)
            {
                equalityComparer = new ReferenceEqualityComparer();
            }

            this.references = new Dictionary<object, CacheItem>(equalityComparer);
            this.maxCapacity = maxCapacity;
            this.maxAge = maxAge;
        }

        /// <summary>
        /// Gets the reference for the sepecified object.
        /// </summary>
        /// <param name="context">The serialization context.</param>
        /// <param name="value">The object to get a reference for.</param>
        /// <returns>The reference to the object.</returns>
        public string GetReference(object context, object value)
        {
            lock (this.objectLock)
            {
                CacheItem item;
                if (!this.references.TryGetValue(value, out item))
                {
                    item = new CacheItem
                    {
                        CreationDate = DateTime.UtcNow,
                        ReferenceId = Guid.NewGuid().ToString()
                    };
                    this.references[value] = item;
                }

                return item.ReferenceId;
            }
        }

        /// <summary>
        /// Determines whether the specified object is referenced.
        /// </summary>
        /// <param name="context">The serialization context.</param>
        /// <param name="value">The object to test for a reference.</param>
        /// <returns>
        /// 	<c>true</c> if the specified object is referenced; otherwise, <c>false</c>.
        /// </returns>
        public bool IsReferenced(object context, object value)
        {
            lock (this.objectLock)
            {
                if (this.references.Count > this.maxCapacity)
                {
                    // cleanup
                    var itemsToRemove = this.references.OrderBy(kvp => kvp.Value.CreationDate)
                        .Take(this.references.Count - this.maxCapacity)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    foreach (var key in itemsToRemove)
                    {
                        this.references.Remove(key);
                    }
                }

                CacheItem item;
                if (!this.references.TryGetValue(value, out item))
                {
                    return false;
                }

                var now = DateTime.UtcNow;
                if (this.maxAge != TimeSpan.MaxValue && item.CreationDate < DateTime.UtcNow - this.maxAge)
                {
                    this.references.Remove(value);
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Adds a reference to the specified object.
        /// </summary>
        /// <param name="context">The serialization context.</param>
        /// <param name="reference">The reference.</param>
        /// <param name="value">The object to reference.</param>
        /// <remarks>Not supported.</remarks>
        public void AddReference(object context, string reference, object value)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Resolves a reference to its object.
        /// </summary>
        /// <param name="context">The serialization context.</param>
        /// <param name="reference">The reference to resolve.</param>
        /// <returns>The object that</returns>
        /// <remarks>Not supported.</remarks>
        public object ResolveReference(object context, string reference)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Internal class to track cached items.
        /// </summary>
        internal class CacheItem
        {
            internal DateTime CreationDate { get; set; }

            internal string ReferenceId { get; set; }
        }
        
        /// <summary>
        /// Default <see cref="IEqualityComparer{T}"/>.
        /// </summary>
        internal class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public new bool Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

             public int GetHashCode(object obj) 
             {
                 // mimicing JSON.NET: https://github.com/JamesNK/Newtonsoft.Json/blob/master/Src/Newtonsoft.Json/Serialization/JsonSerializerInternalBase.cs
 #if !(NETFX_CORE) 
                 // put objects in a bucket based on their reference 
                 return RuntimeHelpers.GetHashCode(obj); 
 #else 
     // put all objects in the same bucket so ReferenceEquals is called on all 
         return -1; 
 #endif 
             } 
        }
    }
}
