namespace Unload.TaskFlow;

/// <summary>
/// Fluent builder для явной настройки task pipeline.
/// </summary>
public sealed class TaskPipelineBuilder
{
    private readonly List<TaskPipelineDescriptor> _descriptors = [];

    /// <summary>
    /// Регистрирует новую задачу в pipeline.
    /// </summary>
    public TaskRegistrationBuilder AddTask<TDefinition>(string taskCode)
    {
        return new TaskRegistrationBuilder(this, taskCode, typeof(TDefinition));
    }

    /// <summary>
    /// Создает итоговую конфигурацию pipeline.
    /// </summary>
    public TaskPipeline Build()
    {
        return new TaskPipeline(_descriptors);
    }

    private void AddDescriptor(TaskPipelineDescriptor descriptor)
    {
        if (_descriptors.Any(x => string.Equals(x.TaskCode, descriptor.TaskCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Task '{descriptor.TaskCode}' is already configured in the pipeline.");
        }

        _descriptors.Add(descriptor);
    }

    /// <summary>
    /// Builder конфигурации отдельной задачи.
    /// </summary>
    public sealed class TaskRegistrationBuilder
    {
        private readonly TaskPipelineBuilder _owner;
        private readonly string _taskCode;
        private readonly Type _definitionType;
        private readonly HashSet<string> _requiredCodes = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _conflictingCodes = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<Type> _transitionHandlerTypes = [];

        internal TaskRegistrationBuilder(TaskPipelineBuilder owner, string taskCode, Type definitionType)
        {
            _owner = owner;
            _taskCode = taskCode;
            _definitionType = definitionType;
        }

        public TaskRegistrationBuilder RequiresCompleted(params string[] codes)
        {
            foreach (var code in codes.Where(static x => !string.IsNullOrWhiteSpace(x)))
            {
                _requiredCodes.Add(code);
            }

            return this;
        }

        public TaskRegistrationBuilder ConflictsWith(params string[] codes)
        {
            foreach (var code in codes.Where(static x => !string.IsNullOrWhiteSpace(x)))
            {
                _conflictingCodes.Add(code);
            }

            return this;
        }

        public TaskRegistrationBuilder AfterCompletion<THandler>()
            where THandler : class, IWorkflowTaskTransitionHandler
        {
            _transitionHandlerTypes.Add(typeof(THandler));
            return this;
        }

        public TaskPipelineBuilder Register()
        {
            _owner.AddDescriptor(new TaskPipelineDescriptor(
                _taskCode,
                _definitionType,
                _requiredCodes.ToArray(),
                _conflictingCodes.ToArray(),
                _transitionHandlerTypes.ToArray()));
            return _owner;
        }
    }
}
