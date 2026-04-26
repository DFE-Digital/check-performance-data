using System.Collections.Generic;
using System.Text;

namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public class UserFieldsResponseDto : BasePagedModelResponseDto
    {
        public List<CustomFieldMetaDataDto> UserFields { get; set; }

        public int MaxUserFieldLimit { get; set; }
    }
}
