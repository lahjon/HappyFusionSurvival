$MAX_ITERATIONS = 50

Write-Host "=== Iteration 1 / $MAX_ITERATIONS ==="
Get-Content -Raw prompt.md | claude -p --dangerously-skip-permissions --max-turns 200

for ($i = 2; $i -le $MAX_ITERATIONS; $i++) {
    Write-Host "=== Iteration $i / $MAX_ITERATIONS ==="
    $output = echo "Continue where you left off. Do not summarize. Do not stop until all 5 phases are complete and balancing targets are met. If everything is truly done, respond with DONE. Otherwise keep coding." | claude -p --dangerously-skip-permissions --max-turns 200 --continue
    
    Write-Host $output
    
    if ($output -match "DONE") {
        Write-Host "=== All phases complete! ==="
        break
    }
    
    Start-Sleep -Seconds 3
}