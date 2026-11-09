using Inceptus.DiagramEngine.Contracts.Primitives;

namespace Inceptus.DiagramEngine.Contracts.Commands;

public sealed class CommandValidatorRegistration
{
    public CommandValidatorRegistration(
        CommandTypeId typeId,
        CommandValidatorId validatorId,
        ICommandValidator validator)
    {
        ArgumentNullException.ThrowIfNull(typeId);
        ArgumentNullException.ThrowIfNull(validatorId);
        ArgumentNullException.ThrowIfNull(validator);

        TypeId = typeId;
        ValidatorId = validatorId;
        Validator = validator;
    }

    public CommandTypeId TypeId { get; }

    public CommandValidatorId ValidatorId { get; }

    public ICommandValidator Validator { get; }
}
