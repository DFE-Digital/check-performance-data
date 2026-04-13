// using DfE.CheckPerformanceData.Application.Features.LandingPage;
// using MediatR;
// using Microsoft.EntityFrameworkCore;
//
// namespace DfE.CheckPerformanceData.Application.Features.CheckingWindows;
//
// public class GetOpenWindowsQueryHandler(IPortalDbContext dbContext, TimeProvider timeProvider) : IRequestHandler<GetOpenWindowsQuery, List<OpenWindowDto>>
// {
//     public async Task<List<OpenWindowDto>> Handle(GetOpenWindowsQuery request, CancellationToken cancellationToken)
//     {
//         var now = timeProvider.GetUtcNow();
//         var windows = await dbContext.CheckingWindows
//             .Where(window 
//                 => window.StartDate <= now 
//                    && window.EndDate >= now 
//                    )
//             .ToListAsync(cancellationToken);
//
//         return windows.Select(w => new OpenWindowDto {Title = w.Title, EndDate = w.EndDate, KeyStage = w.KeyStage}).ToList();
//     }
// }