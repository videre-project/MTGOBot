-- Count the total number of matches and games, grouped by format
SELECT e.format,
       COUNT(*) as matches,
       SUM(ARRAY_LENGTH(m.games, 1)) as games
FROM Matches m
INNER JOIN Events e ON e.id = m.event_id
GROUP BY e.format
ORDER BY games DESC;

-- Query match results with basic event info and archetypes (player, opponent)
SELECT e.date,
       e.format,
       m.event_id,
       e.kind as event_type,
       a1.archetype AS archetype1,
       ARRAY_TO_STRING(ARRAY(
        SELECT CASE
          WHEN game.result = 'win' THEN 'W'
          WHEN game.result = 'loss' THEN 'L'
          WHEN game.result = 'draw' THEN 'T'
        END
        FROM UNNEST(m.games) AS game), '-') AS games,
       m.result,
       a2.archetype AS archetype2
FROM Matches m
INNER JOIN Events e ON e.id = m.event_id
INNER JOIN Decks d1 ON d1.event_id = m.event_id
                    AND d1.player = m.player
INNER JOIN Decks d2 ON d2.event_id = m.event_id
                    AND d2.player = m.opponent
INNER JOIN Archetypes a1 ON a1.deck_id = d1.id
INNER JOIN Archetypes a2 ON a2.deck_id = d2.id
WHERE m.isBye = FALSE
  AND a1.archetype_id IS NOT NULL
  AND a2.archetype_id IS NOT NULL
ORDER BY m.event_id, m.round, m.player
LIMIT 5;
