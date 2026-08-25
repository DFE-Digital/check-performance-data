// Project-wide usings for the Web host. Conservative by design: only high-frequency,
// collision-free namespaces. Known duplicate-name hotspots (CheckingWindowDto x2,
// SessionExtensions x2) are deliberately excluded — do not add their namespaces here.
global using Microsoft.AspNetCore.Mvc;
global using DfE.CheckPerformanceData.Application.Analytics;
global using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
global using DfE.CheckPerformanceData.Web.Admin.Nav;
global using DfE.CheckPerformanceData.Web.Admin;
global using DfE.CheckPerformanceData.Application.Settings;
global using DfE.CheckPerformanceData.Application.Journey;
global using DfE.CheckPerformanceData.Application.CheckYourPupilData;
global using Microsoft.AspNetCore.Authorization;
global using Microsoft.Extensions.Hosting;
global using DfE.CheckPerformanceData.Domain.Enums;
