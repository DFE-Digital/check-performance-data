using System;
using System.Collections.Generic;
// Please install Json.NET (Newtonsoft.Json) NuGet package if not already installed in your project for JSON serialization/deserialization operations.
// You can download it from https://www.newtonsoft.com/json
namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public class CustomFieldMetaDataDto
    {
        // Gets or sets the URL associated with the custom field.
        public string Url { get; set; }

        // Gets or sets the unique identifier for the custom field.

        public long Id { get; set;}

        // Gets or sets the type of the custom field.
        public string Type { get; set; }

        // Gets or sets the key associated with the custom field.
        public string Key { get; set; }

        // Gets or sets the title of the custom field.
        public string Title { get; set; }

        // Gets or sets the description of the custom field.
        public string Description { get; set; }

        // Gets or sets the raw title of the custom field.
        public string RawTitle { get; set; }

        // Gets or sets the raw description of the custom field.
        public string RawDescription { get; set; }

        // Gets or sets the position of the custom field.
        public int Position { get; set; }

        // Gets or sets a value indicating whether the custom field is active.
        public bool Active { get; set; }

        // Gets or sets a value indicating whether the custom field is a system field.
        public bool System { get; set; }

        // Gets or sets the regular expression for validating the custom field value.
        public string RegexpForValidation { get; set; }

        // Gets or sets the creation date of the custom field.
        public DateTime CreatedAt { get; set; }

        // Gets or sets the last update date of the custom field.
        public DateTime UpdatedAt { get; set; }

        // Gets or sets a list of custom field options.
        public List<CustomFieldOptionDto> CustomFieldOptions { get; set;  } = new List<CustomFieldOptionDto>();
    }

    public class CustomFieldOptionDto
    {

        public long Id { get; set; }


        public string Name { get; set; }


        public string RawName { get; set; }


        public string Value { get; set; }

    }

}
