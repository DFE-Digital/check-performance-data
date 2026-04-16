using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace DfE.CheckPerformanceData.ZendeskClient.Refit.Models
{
    public class ListViewsRequest
    {
        /// <summary>
        /// Only views with given access. May be "personal", "shared", or "account"
        /// </summary>
        [JsonProperty("access")]
        public string? Access {  get; set; }

        /// <summary>
        /// Only active views if true, inactive views if false
        /// </summary>
        [JsonProperty("active")]
        public bool? Active {  get; set; }

        /// <summary>
        /// Only views belonging to given group
        /// </summary>
        [JsonProperty("group_id")]
        public int? GroupId { get; set; }

        /// <summary>
        /// The sort parameter used with cursor pagination. Defaults to "created_at". Prefix with '-' for descending order
        /// </summary>
        [JsonProperty("sort")]
        public string? Sort { get; set; }

        /// <summary>
        /// The sort_by parameter used with offset pagination. Possible values are "alphabetical", "created_at", or "updated_at". Defaults to "position"
        /// </summary>
        [JsonProperty("sort_by")]
        public string? SortBy { get; set; }


        /// <summary>
        /// The sort_order parameter used with offset pagination. One of "asc" or "desc". Defaults to "asc" for alphabetical and position sort, "desc" for all others
        /// </summary>
        [JsonProperty("sort_order")]
        public string? SortOrder { get; set; }

        /// <summary>
        /// Pagination parameter. Supports both traditional offset and cursor-based pagination:
        ///- Traditional: `?page=2` (integer page number)
        ///- Cursor: `?page[size]=50&page[after]=cursor` (deepObject with size, after, before)
        ///These are mutually exclusive -use one format or the other, not both.
        /// </summary>
        [JsonProperty("page")]
        public int? Page { get; set; }

        /// <summary>
        /// Number of records to return per page.
        /// </summary>
        [JsonProperty("per_page")]
        public int? PerPage { get; set; }   



    }

 

}
