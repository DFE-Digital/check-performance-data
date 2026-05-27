using System;
using System.Collections.Generic;

namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public static class RequestQueryExtensions
    {
        private static void AddToDictionary(Dictionary<string, object> dict, string key, string? value)
        {
            if (!string.IsNullOrEmpty(value))
                dict[key] = value;
        }

        private static void AddToDictionary(Dictionary<string, object> dict, string key, int? value)
        {
            if (value.HasValue)
                dict[key] = value.Value;
        }

        private static void AddToDictionary(Dictionary<string, object> dict, string key, bool? value)
        {
            if (value.HasValue)
                dict[key] = value.Value;
        }

        public static Dictionary<string, object> ToQueryDictionary(this ListViewsRequestDto request)
        {
            var dict = new Dictionary<string, object>();

            AddToDictionary(dict, "access", request.Access);
            AddToDictionary(dict, "active", request.Active);
            AddToDictionary(dict, "group_id", request.GroupId);
            AddToDictionary(dict, "sort", request.Sort);
            AddToDictionary(dict, "sort_by", request.SortBy);
            AddToDictionary(dict, "sort_order", request.SortOrder);
            AddToDictionary(dict, "page", request.Page);
            AddToDictionary(dict, "per_page", request.PerPage);

            return dict;
        }

        public static Dictionary<string, object> ToQueryDictionary(this ListViewTicketsRequestDto request)
        {
            var dict = new Dictionary<string, object>();

            AddToDictionary(dict, "page", request.Page);
            AddToDictionary(dict, "per_page", request.PerPage);
            AddToDictionary(dict, "sort_by", request.SortBy);
            AddToDictionary(dict, "sort_order", request.SortOrder);

            return dict;
        }
    }
}
