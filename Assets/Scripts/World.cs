﻿using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Priority_Queue;

public class World : MonoBehaviour
{
	[SerializeField] private GameObject _chunkPrefab;
	private static Dictionary<Vector3Int, DataChunk> _chunks = new Dictionary<Vector3Int, DataChunk>();
	private static Dictionary<Vector2Int, DataColumn> _columns = new Dictionary<Vector2Int, DataColumn>();
	private static Dictionary<Vector3Int, DataChunk> _offloadChunks = new Dictionary<Vector3Int, DataChunk>();
	private SimplePriorityQueue<Chunk> _loadQueue = new SimplePriorityQueue<Chunk>();
	private bool _rendering;

	[SerializeField] private static int _chunkSize = 16;
	[SerializeField] private static int _viewRangeHorizontal = 3;
	[SerializeField] private static int _viewRangeVertical = 3;
	private static Vector3Int _playerPos;

	void Start()
	{
		_playerPos = Vector3Int.FloorToInt(Camera.main.transform.position / _chunkSize);

		GenerateChunks();

		#if UNITY_EDITOR
		UnityEditor.SceneView.FocusWindowIfItsOpen(typeof(UnityEditor.SceneView));
		#endif
	}

	void Update()
	{
		Vector3Int temp = Vector3Int.FloorToInt(Camera.main.transform.position / _chunkSize);

		// Did player move from their chunk?
		if (CubeDistance(temp, _playerPos) > 0)
		{
			_playerPos = temp; // Set new pos
			GenerateChunks(); // Generate new chunks
			PingChunks(); // Ping old chunks for deletion
		}
	}

	private void RenderThread()
	{
		while (_loadQueue.Count > 0)
		{
			Chunk newChunkScript = _loadQueue.Dequeue();

			if (newChunkScript != null)
			{
				// Errors in threads need to be manually caught and sent to the main thread
				try
				{
					newChunkScript.GenerateBlocks();
				}
				catch(Exception ex)
				{
					Debug.LogException(ex);
				}
			}
		}

		_rendering = false;
	}

	private void GenerateChunks()
	{
		// Which direction is the player pointing in?
		Vector3 pov = Camera.main.transform.rotation * Vector3.forward;
		pov.y = 0; // Flatten it as we want it to be horizontal

		// Iterate through x, y, z
		for (int x = _playerPos.x - _viewRangeHorizontal - 1; x <= _playerPos.x + _viewRangeHorizontal + 1; ++x)
		{
			for (int z = _playerPos.z - _viewRangeHorizontal - 1; z <= _playerPos.z + _viewRangeHorizontal + 1; ++z)
			{
				Vector2Int grid = new Vector2Int(x, z);

				DataColumn newDataColumn;

				// Does column exist?
				if (!_columns.ContainsKey(grid))
				{
					// Create new data column
					newDataColumn = new DataColumn(grid);

					// Store in map
					_columns[grid] = newDataColumn;
				}
				else
				{
					newDataColumn = _columns[grid];
				}

				for (int y = _playerPos.y - _viewRangeVertical - 1; y <= _playerPos.y + _viewRangeVertical + 1; ++y)
				{
					Vector3Int pos = new Vector3Int(x, y, z);
					
                    // Does chunk exist?
					if (!_chunks.ContainsKey(pos) && Distance(pos, _playerPos) <= _viewRangeHorizontal)
					{
						// Create new chunk and get corresponding script
						GameObject newChunk = Instantiate(_chunkPrefab, new Vector3(x * _chunkSize, y * _chunkSize, z * _chunkSize), Quaternion.identity);
						Chunk newChunkScript = newChunk.GetComponent<Chunk>();

						DataChunk newDataChunk;

						if (_offloadChunks.ContainsKey(pos))
						{
							// Retrieve from offload
							newDataChunk = _offloadChunks[pos];

							// Give data chunk gameobject
							newDataChunk.SetChunk(newChunkScript);

							// Remove from offload
							_offloadChunks.Remove(pos);
						}
						else
						{
							// Create new data chunk
							newDataChunk = new DataChunk(pos, newChunkScript, newDataColumn);
						}

						// Let chunk know its corresponding data chunk and position
						newChunkScript.LoadData(pos, newDataChunk);

						// Should chunk render yet?
						//newChunkScript.SetRender(CubeDistance(_playerPos, pos) <= _viewRangeHorizontal);

						// Get angle difference between vectors
						Vector3 dir = pos * _chunkSize - Camera.main.transform.position;
						float dist = dir.magnitude;
						float diff = Vector3.Angle(pov, dir);
						float final = dist + diff;
						if (dist < _chunkSize * 2f) // Prioritize chunks immediately closest
						{
							final = dist;
						}

						// Queue chunk for generation
						_loadQueue.Enqueue(newChunkScript, final);

						// Store in map
						_chunks[pos] = newDataChunk;
					}
				}
			}
		}

		// Are there chunks that need generation?
		if (!_rendering && _loadQueue.Count > 0)
		{
			_rendering = true;
			new Thread(RenderThread).Start();
		}
	}

	private void PingChunks()
	{
		List<Vector3Int> temp = new List<Vector3Int>();

        // Collect all chunks that need to be deleted
		foreach (KeyValuePair<Vector3Int, DataChunk> pair in _chunks)
		{
			if (CubeDistance(pair.Key, _playerPos) > _viewRangeHorizontal + 1)
			{
				temp.Add(pair.Key);
			}
			else
			{
				/*
				// Get chunk
				Chunk chunkScript = pair.Value.GetChunk();

				// Make only hidden chunks render!
				if (chunkScript.GetRender())
				{
					// Should chunk render yet?
					chunkScript.SetRender(CubeDistance(_playerPos, pair.Key) <= _viewRangeHorizontal);

					// Queue chunk for generation
					_loadQueue.Enqueue(chunkScript, 0);
				}
				*/
			}
		}

		// Are there chunks that need generation?
		if (!_rendering && _loadQueue.Count > 0)
		{
			_rendering = true;
			new Thread(RenderThread).Start();
		}

		// Destroy chunk
		foreach (Vector3Int key in temp)
		{
			DestroyChunk(key);
		}
	}

	private void DestroyChunk(Vector3Int pos)
	{
		Destroy(_chunks[pos].GetChunk()); // Delete corresponding gameobject
		//_offloadChunks[pos] = _chunks[pos]; //Move chunk data to offload—technically should be disk or something
		_chunks.Remove(pos); // Remove chunk from main list
	}

	// This gets blocks that have already been generated in the past
	public static Atlas.ID GetBlock(Vector3Int pos, int x, int y, int z)
	{
		return _chunks[pos].GetBlock(x, y, z);
	}

	// This is the main world generation function per block
	public static Atlas.ID GenerateBlock(DataColumn column, int x, int y, int z)
	{
		// Topology
		float stone = column.GetSurface(x, z);
		float dirt = 3;
		
		if (y <= stone)
		{
			// Caves
			float caves = PerlinNoise(x, y * 2, z, 40, 12, 1);
			caves += PerlinNoise(x, y, z, 30, 8, 0);
			caves += PerlinNoise(x, y, z, 10, 4, 0);
			
			if (caves > 16)
			{
				return Atlas.ID.Air; // Generating caves
			}

			// Underground ores
			float coal = PerlinNoise(x, y, z, 20, 20, 0);

			if (coal > 18)
			{
				return Atlas.ID.Coal;
			}

			return Atlas.ID.Stone; // Stone layer
		}
		else if (y <= stone + dirt)
		{
			return Atlas.ID.Dirt; // Dirt cover
		}
		else if (y <= stone + dirt + 1)
		{
			return Atlas.ID.Grass; // Grass cover
		}
		else
		{
			return Atlas.ID.Air; // Open Air
		}
	}

	public static int GenerateTopology(int x, int z)
	{
		// Topology
		float stone = PerlinNoise(x, 0, z, 10, 3, 1.2f);
		stone += PerlinNoise(x, 300, z, 20, 4, 1f);
		stone += PerlinNoise(x, 500, z, 100, 20, 1f);

		// "Plateues"
		if (PerlinNoise(x, 100, z, 100, 10, 1f) >= 9f)
		{
			stone += 10;
		}

		stone += Mathf.Clamp(PerlinNoise(x, 0, z, 50, 10, 5f), 0, 10); // Craters?
		//float dirt = PerlinNoise(x, 100, z, 50, 2, 0) + 3; // At least 3 dirt
		//float dirt = 3;

		return (int) stone;
	}

	public static float PerlinNoise(float x, float y, float z, float scale, float height, float power)
	{
		float rValue;
		rValue = Noise.Noise.GetNoise(((double)x) / scale, ((double)y) / scale, ((double)z) / scale);
		rValue *= height;

		if (power != 0)
		{
			rValue = Mathf.Pow(rValue, power);
		}

		return rValue;
	}

	public static int GetChunkSize()
	{
		return _chunkSize;
	}

	public static int GetViewRange()
	{
		return _viewRangeHorizontal;
	}

	public static DataChunk GetChunk(Vector3Int chunkPos)
	{
		DataChunk chunk;
		_chunks.TryGetValue(chunkPos, out chunk);
		return chunk;
	}

	{
	}
}
