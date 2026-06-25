using InventoryPlanning.Api.Host.Contracts;
using InventoryPlanning.Api.Host.Services;
using Microsoft.AspNetCore.Mvc;

namespace InventoryPlanning.Api.Host.Controllers;

[ApiController]
[Route("api/inventory-planning")]
public sealed class InventoryPlanningController : ControllerBase
{
    private readonly InventoryPlanningWorkflowService _basicWorkflowService;
    private readonly BlobDocumentStorageService _documentStorageService;
    private readonly ILogger<InventoryPlanningController> _logger;

    public InventoryPlanningController(
        InventoryPlanningWorkflowService basicWorkflowService,
        BlobDocumentStorageService documentStorageService,
        ILogger<InventoryPlanningController> logger)
    {
        _basicWorkflowService = basicWorkflowService;
        _documentStorageService = documentStorageService;
        _logger = logger;
    }

    [HttpPost("cases/{caseId}/workflow/basic/start")]
    public async Task<ActionResult<BasicWorkflowStatusResponse>> StartBasicWorkflowAsync(
        string caseId,
        CancellationToken cancellationToken)
    {
        string executionId = Guid.NewGuid().ToString("N");

        try
        {
            _logger.LogInformation(
                "Starting basic workflow for case {CaseId} with execution {ExecutionId}.",
                caseId,
                executionId);

            BasicWorkflowStatusResponse response = await _basicWorkflowService.StartBasicWorkflowAsync(
                caseId,
                executionId,
                cancellationToken);

            return Ok(response);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetailsResponse
            {
                Title = "Planning case not found.",
                Detail = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetailsResponse
            {
                Title = "Basic workflow cannot be started.",
                Detail = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to start basic inventory planning workflow for case {CaseId} with execution {ExecutionId}.",
                caseId,
                executionId);

            return Problem(
                detail: ex.Message,
                title: "Basic inventory planning workflow failed to start.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    [HttpGet("cases/{caseId}/documents")]
    public async Task<ActionResult<CaseDocumentsResponse>> GetCaseDocumentsAsync(
        string caseId,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<CaseDocumentInfo> documents = await _documentStorageService.ListCaseDocumentsAsync(
                caseId,
                cancellationToken);

            if (documents.Count == 0)
            {
                return NotFound(new ProblemDetailsResponse
                {
                    Title = "Planning case not found.",
                    Detail = $"Case '{caseId}' was not found in Blob Storage or has no documents under prefix '{BlobDocumentStorageService.GetCasePrefix(caseId)}'."
                });
            }

            return Ok(new CaseDocumentsResponse
            {
                CaseId = caseId.Trim(),
                Documents = documents
                    .Select(document => new CaseDocumentResponse
                    {
                        FileName = document.FileName,
                        ContentType = document.ContentType,
                        BlobName = document.BlobName,
                        Reference = document.Reference,
                        LastModifiedUtc = document.LastModifiedUtc
                    })
                    .ToArray()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to list documents for case {CaseId}.",
                caseId);

            return Problem(
                detail: ex.Message,
                title: "Failed to list case documents.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    [HttpGet("cases/{caseId}/documents/content")]
    public async Task<IActionResult> GetCaseDocumentContentAsync(
        string caseId,
        [FromQuery] string blobName,
        CancellationToken cancellationToken)
    {
        try
        {
            LoadedCaseDocument document = await _documentStorageService.GetCaseDocumentAsync(
                caseId,
                blobName,
                cancellationToken);

            return File(
                document.Content.ToArray(),
                document.ContentType,
                document.FileName);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetailsResponse
            {
                Title = "Planning case document not found.",
                Detail = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetailsResponse
            {
                Title = "Planning case document request is invalid.",
                Detail = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to load document {BlobName} for case {CaseId}.",
                blobName,
                caseId);

            return Problem(
                detail: ex.Message,
                title: "Failed to load case document.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    [HttpGet("executions/{executionId}/basic/status")]
    public ActionResult<BasicWorkflowStatusResponse> GetStatusBasicWorkflowAsync(string executionId)
    {
        try
        {
            return Ok(_basicWorkflowService.GetBasicWorkflowStatus(executionId));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetailsResponse
            {
                Title = "Basic workflow execution not found.",
                Detail = ex.Message
            });
        }
    }

    [HttpPost("cases/{caseId}/workflow/basic/executions/{executionId}/resume")]
    public ActionResult<BasicWorkflowStatusResponse> ResumeBasicWorkflowAsync(
        string caseId,
        string executionId,
        [FromBody] BasicWorkflowApprovalRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            BasicWorkflowStatusResponse response = _basicWorkflowService.ResumeBasicWorkflowAsync(
                caseId,
                executionId,
                request.Approved,
                request.ReviewerComment,
                cancellationToken);

            return Ok(response);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetailsResponse
            {
                Title = "Basic workflow execution not found.",
                Detail = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetailsResponse
            {
                Title = "Basic workflow cannot be resumed.",
                Detail = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to resume basic workflow for case {CaseId} with execution {ExecutionId}.",
                caseId,
                executionId);

            return Problem(
                detail: ex.Message,
                title: "Basic workflow failed to resume.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
