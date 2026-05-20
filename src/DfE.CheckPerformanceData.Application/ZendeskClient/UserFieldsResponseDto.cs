using System.Collections.Generic;

namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public sealed class UserFieldsResponseDto : BasePagedModelResponseDto
    {
        public List<CustomFieldMetaDataDto> UserFields { get; set; } = new();

        public int MaxUserFieldLimit { get; set; }
    }
}
