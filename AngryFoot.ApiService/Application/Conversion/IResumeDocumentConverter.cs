namespace AngryFoot.ApiService.Application.Conversion;

/// <summary>
/// Turns an uploaded resume into Markdown for <see cref="Bullets.ResumeBulletParser"/>, which is why
/// the return type is text rather than a parsed shape: conversion and parsing stay separable, and a
/// converted document takes exactly the path a pasted one does from here on.
/// </summary>
internal interface IResumeDocumentConverter
{
    bool IsAvailable { get; }

    Task<string> ConvertAsync(Stream content, string fileName, CancellationToken cancellationToken);
}

/// <summary>
/// A conversion failure whose message is written for the person who uploaded the file. Plays the
/// role <c>InvalidLinkedInExportException</c> plays for the LinkedIn import: the endpoint maps it to
/// a 400 and the page shows it verbatim.
/// </summary>
internal sealed class ResumeConversionException(string message) : Exception(message);
