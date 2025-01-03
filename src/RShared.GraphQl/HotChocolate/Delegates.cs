using HotChocolate.Types;

namespace RShared.GraphQl.HotChocolate;


public delegate void SchemaOperationRegistrationDelegate(IObjectTypeDescriptor descriptor);

public delegate Task<object?> OperationProcessDelegate(IServiceProvider serviceProvider, object?[] @params, Type? responseType, CancellationToken cancellationToken = default);