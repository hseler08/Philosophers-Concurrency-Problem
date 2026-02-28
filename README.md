# Philosophers-Concurrency-Problem
# Philosophers-Concurrency-Problem

A C# implementation of the classic **Dining Philosophers** problem. This project demonstrates two different strategies for thread synchronization and deadlock avoidance using the **Task Parallel Library (TPL)**.

## The Problem Overview
At a round table sit 5 (or more) philosophers. There is a single fork between each pair. A philosopher needs **two forks** (left and right) to eat. If every philosopher picks up their left fork at the same time, no one can pick up their right fork—resulting in a system freeze known as a **deadlock**.


## How to Run
1.  Ensure you have the **.NET SDK** installed (recommended .NET 6.0 or newer).
2.  Clone the repository:
    ```bash
    git clone [https://github.com/your-username/Philosophers-Concurrency-Problem.git](https://github.com/your-username/Philosophers-Concurrency-Problem.git)
    ```
3.  Navigate to the project folder and run:
    ```bash
    dotnet run
    ```
4.  Once the simulation finishes (approx. 12s per strategy), results are saved to `wyniki.txt`.

## Implemented Strategies

### 1. AtomicBothStrategy (All or Nothing)
The philosopher checks if **both** required "bathrooms" are free. If yes, they enter both at once. If even one is occupied, they enter neither, wait for a moment (`Thread.Sleep`), and try again later.
* **Pro:** Extremely safe, zero risk of deadlock.
* **Con:** Can lead to higher CPU usage due to frequent checking (busy-waiting).

### 2. OrderedLockingStrategy (Resource Hierarchy)
We introduce a rule: everyone must first knock on the bathroom door with the **lower number**.
* By forcing this order, the last philosopher won't "close the circle" of waiting. Instead of grabbing fork #5 then #0, they must wait for #0 first.
* This is highly efficient and solves the deadlock at the system level.

## Metrics & Statistics
The program tracks:
* **WaitTicks**: Total time spent waiting for "locked doors."
* **Meals**: Total number of successful eating sessions.
* **avg_wait_ms**: Average wait time per meal (lower is better).

*Created for educational purposes to master multi-threading and concurrency in C#.*
