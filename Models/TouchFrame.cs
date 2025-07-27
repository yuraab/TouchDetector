using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CPRTouchVision.Models
{
    /// <summary>
    /// Represents a snapshot of detected touch clusters for a single frame.
    /// </summary>
    public class TouchFrame
    {
        /// <summary>
        /// List of detected touch clusters.
        /// </summary>
        public IReadOnlyList<TouchCluster> Clusters { get; }

        /// <summary>
        /// Time when this frame was generated (UTC recommended).
        /// </summary>
        public long Timestamp { get; }

        /// <summary>
        /// Indicates whether any touch clusters were detected.
        /// </summary>
        public bool HasTouches => Clusters.Count > 0;

        /// <summary>
        /// Initializes a new <see cref="TouchFrame"/> with detected clusters and timestamp.
        /// </summary>
        /// <param name="clusters">List of detected clusters.</param>
        /// <param name="timestamp">The time of capture in microseconds.</param>
        public TouchFrame(IEnumerable<TouchCluster> clusters, long timestamp)
        {
            if (clusters == null)
                throw new ArgumentNullException(nameof(clusters));

            Clusters = new ReadOnlyCollection<TouchCluster>(new List<TouchCluster>(clusters));
            Timestamp = timestamp;
        }

        /// <summary>
        /// Returns string representation of the frame.
        /// </summary>
        public override string ToString()
        {
            return $"TouchFrame(Time={Timestamp:HH:mm:ss.fff}, Clusters={Clusters.Count})";
        }
    }
}
