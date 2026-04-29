using System;
using System.Collections.Generic;
using System.Text;

namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public class ListViewsRequestDto
    {
        /// <summary>
        /// Only views with given access. May be "personal", "shared", or "account"
        /// </summary>
        public string? Access { get; set; }

        /// <summary>
        /// Only active views if true, inactive views if false
        /// </summary>
        public bool? Active { get; set; }

        /// <summary>
        /// Only views belonging to given group
        /// </summary>
        public int? GroupId { get; set; }

        /// <summary>
        /// The sort parameter used with cursor pagination. Defaults to "created_at". Prefix with '-' for descending order
        /// </summary>
        public string? Sort { get; set; }

        /// <summary>
        /// The sort_by parameter used with offset pagination. Possible values are "alphabetical", "created_at", or "updated_at". Defaults to "position"
        /// </summary>
        public string? SortBy { get; set; }

        /// <summary>
        /// The sort_order parameter used with offset pagination. One of "asc" or "desc". Defaults to "asc" for alphabetical and position sort, "desc" for all others
        /// </summary>
        public string? SortOrder { get; set; }

        /// <summary>
        /// Traditional: page number (integer). Cursor: used with page[size] and page[after].
        /// These are mutually exclusive formats.
        /// </summary>
        public int? Page { get; set; }

        /// <summary>
        /// Number of records to return per page.
        /// </summary>
        public int? PerPage { get; set; }
    }
}
