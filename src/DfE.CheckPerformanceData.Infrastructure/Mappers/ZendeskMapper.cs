using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models;
using Riok.Mapperly.Abstractions;
using System;
using System.Collections.Generic;

namespace DfE.CheckPerformanceData.Infrastructure.Mappers
{
    [Mapper(RequiredMappingStrategy = RequiredMappingStrategy.None)]
    internal static partial class ZendeskMapper
    {
        public static partial UpdateTicketResponseDto ToDto(UpdateTicketResponse entity);

        public static partial TicketCommentDto ToDto(TicketComment entity);

        public static partial ListViewsResponseDto ToDto(ListViewsResponse entity);

        public static partial ListViewTicketsResponseDto ToDto(ListViewTicketsResponse entity);

        public static partial GetTicketResponseDto ToDto(GetTicketResponse entity);

        public static partial TicketFieldsResponseDto ToDto(TicketFieldsResponse entity);

        public static partial CustomFieldMetaDataDto ToDto(CustomFieldMetaData entity);

        public static partial UserFieldsResponseDto ToDto(UserFieldsResponse entity);

        public static partial CreateTicketRequest ToEntity(CreateTicketRequestDto entity);

        public static partial CreateTicketResponseDto ToDto(CreateTicketResponse entity);

        public static partial TicketCommentsResponseDto ToDto(TicketCommentsResponse entity);
    }
}
