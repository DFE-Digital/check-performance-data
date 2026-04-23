using System;
using System.Collections.Generic;
using System.Text;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models
{
    public static class RequestQueryExtensions
    {
        public static Dictionary<string, object> ToQueryDictionary(this ListViewsRequest request)
        {
            var dict = new Dictionary<string, object>();

            void Add(string key, object value)
            {
                if (value != null)
                    dict[key] = value;
            }

            Add("access", request.Access);
            Add("active", request.Active);
            Add("group_id", request.GroupId);
            Add("sort", request.Sort);
            Add("sort_by", request.SortBy);
            Add("sort_order", request.SortOrder);
            Add("page", request.Page);
            Add("per_page", request.PerPage);

            return dict;
        }

        public static Dictionary<string, object> ToQueryDictionary(this ListViewTicketsRequest request)
        {
            var dict = new Dictionary<string, object>();

            void Add(string key, object value)
            {
                if (value != null)
                    dict[key] = value;
            }

            Add("page", request.Page);
            Add("per_page", request.PerPage);
            Add("sort_by", request.SortBy);
            Add("sort_order", request.SortOrder);

            return dict;
        }
    }
}
