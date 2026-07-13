using System.Text;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

public class ValidateWindowController(IWindowService windowService, ICsvSchemaFileProcessor processor): Controller
{
    private const string PageView = "~/Views/WindowAdmin/Validate.cshtml";

    [HttpGet("admin/windows/{id:guid}/validate")]
    public async Task<IActionResult> Index(Guid id, CancellationToken cancellationToken)
    {
        //step 1: get the window
        //step 2: check the checksum on both files, ingressFile is in {id}/ingress/{ingressfile}, schemaFile is in {id}/schema/{schemafile}
        //step 3: if the checksums return error on checksum clear the file and return to page asking to reupload file.
        //step 4: if the checksums ok, display start checking page.
        
        ValidationViewModel model = new ValidationViewModel()
        {
            WindowId = id,
            ProcessingResult = new ProcessingResult(0, 0, 0, new StringBuilder())
            
        };
        return View(PageView, model);
    }
    
    [HttpPost("admin/windows/{id:guid}/validate")]
    public async Task<IActionResult> Validate(Guid id, CancellationToken cancellationToken)
    {
        //step 1: grab the 2 files from storage
        //step 2: validate the checksums
        //step 3: if checksums fail, display Check sum failed and stop
        //step 4: if checksums ok, display Check sum passed and stop.
        CheckingWindowDto window = await windowService.GetByIdAsync(id, cancellationToken);
        ProcessingResult result = await processor.ProcessAsync(window.Id, window.IngressFile, window.SchemaFile, cancellationToken);
        ValidationViewModel model = new ValidationViewModel()
        {
            WindowId = id,
            ProcessingResult = result
            
        };
        return View(PageView, model);
    }
}