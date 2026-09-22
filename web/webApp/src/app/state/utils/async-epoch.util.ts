/**
 * Small generation gate for overlapping refresh/bootstrap requests. A result
 * may be applied only by the most recently started generation.
 */
export class AsyncEpoch {
  private current = 0;

  begin(): number {
    this.current += 1;
    return this.current;
  }

  isCurrent(epoch: number): boolean {
    return epoch === this.current;
  }
}
