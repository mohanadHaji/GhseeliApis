using System.Text.Json.Serialization;

namespace Ghseeli.BusinessApi.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AddonSelectionType
{
    /// <summary>
    /// At most one active choice may be selected.
    /// </summary>
    SingleChoice = 0,

    /// <summary>
    /// Multiple active choices may be selected up to the configured maximum.
    /// </summary>
    MultipleChoice = 1,

    /// <summary>
    /// Choices behave like counters and the total quantity is validated against the configured minimum and maximum.
    /// </summary>
    QuantityCounter = 2,

    /// <summary>
    /// Exactly one active choice is included automatically and cannot change the base price or duration.
    /// </summary>
    FixedIncludedChoice = 3,

    /// <summary>
    /// A single-choice group intended for segmented or single-button presentation.
    /// </summary>
    SegmentedSingleButtonChoice = 4
}
