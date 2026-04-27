using DfE.CheckPerformanceData.Application.Wiki;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models;
using Riok.Mapperly.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace DfE.CheckPerformanceData.Infrastructure.Mappers
{
    [Mapper(RequiredMappingStrategy = RequiredMappingStrategy.None)]
    internal static partial class ZendeskMapper
    {
        [MapperRequiredMapping(RequiredMappingStrategy.None)]
        public static partial UpdateTicketResponseDto ToDto(UpdateTicketResponse entity);

        [MapperRequiredMapping(RequiredMappingStrategy.None)]
        public static partial TicketCommentDto ToDto(TicketComment entity);
        
    }
}
