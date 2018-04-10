﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DataChunk
{
	private readonly Vector3Int _pos;
	private Chunk _chunk;
	private Atlas.ID[,,] _blocks;
	private DataColumn _column;

	private bool _generated;
	private int _density;

	public DataChunk(Vector3Int pos, Chunk chunk, DataColumn column)
	{
		_pos = pos;
		_chunk = chunk;
		_blocks = new Atlas.ID[World.chunkSize, World.chunkSize, World.chunkSize];
		_column = column; //_columns[_pos];

		_generated = false;
		_density = 0;
	}

	public void GenerateBlocks()
	{
		for (int x = 0; x < World.chunkSize; ++x)
		{
			for (int y = 0; y < World.chunkSize; ++y)
			{
				for (int z = 0; z < World.chunkSize; ++z)
				{
					Atlas.ID block = World.GenerateBlock(_column, _pos.x * World.GetChunkSize() + x, _pos.y * World.GetChunkSize() + y, _pos.z * World.GetChunkSize() + z);

					// Skip air
					if (block == Atlas.ID.Air)
					{
						continue;
					}
					_blocks[x, y, z] = block;

					++_density;
				}
			}
		}

		if (_density == 0)
		{
			_blocks = null;
		}

		_generated = true;
		_chunk.UpdateState();
	}

	public void SetBlock(Atlas.ID block, int x, int y, int z)
	{
		// Do not give us air!
		if (block == Atlas.ID.Air) { return; }

		// Unnullify
		if (_blocks == null)
		{
			_blocks = new Atlas.ID[World.chunkSize, World.chunkSize, World.chunkSize];
		}

		_blocks[x, y, z] = block;

		++_density;
	}

	public void RemoveBlock(int x, int y, int z)
	{
		// Already empty!
		if (_blocks == null || _blocks[x, y, z] == Atlas.ID.Air) { return; }

		_blocks[x, y, z] = Atlas.ID.Air;

		// Check for nullification
		if (--_density == 0)
		{
			_blocks = null;
		}
	}

	public Atlas.ID GetBlock(int x, int y, int z)
	{
		// Empty!
		if (_blocks == null)
		{
			return Atlas.ID.Air;
		}

		// Out of bounds!
		if (x < 0 || x >= World.GetChunkSize() || y < 0 || y >= World.GetChunkSize() || z < 0 || z >= World.GetChunkSize())
		{
			Debug.LogError("Out of bounds! " + x + ", " + y + ", " + z);
			return Atlas.ID.Solid;
		}

		return _blocks[x, y, z];
	}

	public void SetChunk(Chunk chunk)
	{
		_chunk = chunk;
	}

	public Chunk GetChunk()
	{
		return _chunk;
	}

	public DataColumn GetColumn()
	{
		return _column;
	}

	public bool IsGenerated()
	{
		return _generated;
	}

	public bool IsEmpty()
	{
		return (_density == 0);
	}
}