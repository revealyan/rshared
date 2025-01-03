using HotChocolate;

namespace RShared.GraphQl.HotChocolate;

internal sealed class SimpleGraphQlError
	: IError
{
	private readonly Exception _exception;

	public SimpleGraphQlError(Exception exception)
	{
		_exception = exception;
	}

	public string Message => _exception.Message;

	public string? Code => null;

	public global::HotChocolate.Path? Path => null;

	public IReadOnlyList<Location>? Locations => null;

	public IReadOnlyDictionary<string, object?>? Extensions => null;

	public Exception? Exception => _exception;

	public IError RemoveCode() => this;

	public IError RemoveException() => this;

	public IError RemoveExtension(string key) => this;

	public IError RemoveExtensions() => this;

	public IError RemoveLocations() => this;

	public IError RemovePath() => this;

	public IError SetExtension(string key, object? value) => this;

	public IError WithCode(string? code) => this;

	public IError WithException(Exception? exception) => this;

	public IError WithExtensions(IReadOnlyDictionary<string, object?> extensions) => this;

	public IError WithLocations(IReadOnlyList<Location>? locations) => this;

	public IError WithMessage(string message) => this;

	public IError WithPath(global::HotChocolate.Path? path) => this;

	public IError WithPath(IReadOnlyList<object>? path) => this;
}
