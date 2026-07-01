using System;
using System.Collections.Generic;
using KSPClone.Construction;
using Npgsql;
using NpgsqlTypes;

namespace KSPClone.Persistence
{
    /// <summary>
    /// Write-through store for Designs and their part trees (M3-T05, BUILD-1/2,
    /// ADR-0007). A Design is durable construction state in the single
    /// authoritative Postgres. Stored relationally (design + design_node rows) —
    /// no JSON dependency — and restored exactly on server restart. A crash
    /// mid-write leaves a consistent (tree, seq) pair: the whole node set is
    /// replaced inside one transaction.
    ///
    /// Op-log-as-audit (a design_op table) is deferred; the folded tree + seq is
    /// the restore-of-record, which reproduces the identical tree and next seq.
    /// </summary>
    public sealed class DesignStore
    {
        private readonly string _connectionString;

        public DesignStore(string connectionString) => _connectionString = connectionString;

        public void Migrate()
        {
            const string sql = @"
                CREATE TABLE IF NOT EXISTS design (
                    id            UUID   PRIMARY KEY,
                    name          TEXT   NOT NULL DEFAULT '',
                    root_node_id  BIGINT NOT NULL,
                    next_node_id  BIGINT NOT NULL,
                    applied_seq   BIGINT NOT NULL DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS design_node (
                    design_id      UUID   NOT NULL REFERENCES design(id) ON DELETE CASCADE,
                    node_id        BIGINT NOT NULL,
                    part_type      TEXT   NOT NULL,
                    parent_node_id BIGINT NOT NULL,  -- 0 = root (NodeId.None)
                    attach         TEXT   NOT NULL DEFAULT '',
                    px DOUBLE PRECISION NOT NULL, py DOUBLE PRECISION NOT NULL, pz DOUBLE PRECISION NOT NULL,
                    qx DOUBLE PRECISION NOT NULL, qy DOUBLE PRECISION NOT NULL, qz DOUBLE PRECISION NOT NULL, qw DOUBLE PRECISION NOT NULL,
                    PRIMARY KEY (design_id, node_id)
                );
                CREATE INDEX IF NOT EXISTS design_node_design_idx ON design_node (design_id);
            ";
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Write through the whole Design (row + full node set) in one transaction.</summary>
        public void UpsertDesign(Design design)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            using var tx = conn.BeginTransaction();

            using (var d = new NpgsqlCommand(@"
                INSERT INTO design (id, name, root_node_id, next_node_id, applied_seq)
                VALUES (@id, @name, @root, @next, @seq)
                ON CONFLICT (id) DO UPDATE SET
                    name = EXCLUDED.name, root_node_id = EXCLUDED.root_node_id,
                    next_node_id = EXCLUDED.next_node_id, applied_seq = EXCLUDED.applied_seq;", conn, tx))
            {
                d.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = design.Id.Value });
                d.Parameters.AddWithValue("name", design.Name ?? string.Empty);
                d.Parameters.AddWithValue("root", design.RootNodeId.Value);
                d.Parameters.AddWithValue("next", design.PeekNextNodeId);
                d.Parameters.AddWithValue("seq", design.AppliedSeq);
                d.ExecuteNonQuery();
            }

            // Replace the node set wholesale — small trees, always-consistent.
            using (var del = new NpgsqlCommand("DELETE FROM design_node WHERE design_id = @id;", conn, tx))
            {
                del.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = design.Id.Value });
                del.ExecuteNonQuery();
            }
            foreach (var nodeId in design.Tree.Subtree(design.RootNodeId))
            {
                design.Tree.TryGet(nodeId, out var n);
                using var ins = new NpgsqlCommand(@"
                    INSERT INTO design_node
                        (design_id, node_id, part_type, parent_node_id, attach, px,py,pz,qx,qy,qz,qw)
                    VALUES (@did,@nid,@pt,@parent,@attach,@px,@py,@pz,@qx,@qy,@qz,@qw);", conn, tx);
                ins.Parameters.Add(new NpgsqlParameter("did", NpgsqlDbType.Uuid) { Value = design.Id.Value });
                ins.Parameters.AddWithValue("nid", n.Id.Value);
                ins.Parameters.AddWithValue("pt", n.PartType.Value);
                ins.Parameters.AddWithValue("parent", n.Parent.Value);
                ins.Parameters.AddWithValue("attach", n.AttachPoint ?? string.Empty);
                var p = n.LocalPose;
                ins.Parameters.AddWithValue("px", p.Px); ins.Parameters.AddWithValue("py", p.Py); ins.Parameters.AddWithValue("pz", p.Pz);
                ins.Parameters.AddWithValue("qx", p.Qx); ins.Parameters.AddWithValue("qy", p.Qy); ins.Parameters.AddWithValue("qz", p.Qz); ins.Parameters.AddWithValue("qw", p.Qw);
                ins.ExecuteNonQuery();
            }
            tx.Commit();
        }

        public IReadOnlyList<DesignId> LoadDesignIds()
        {
            var ids = new List<DesignId>();
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            using var cmd = new NpgsqlCommand("SELECT id FROM design;", conn);
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read()) ids.Add(new DesignId(rdr.GetGuid(0)));
            return ids;
        }

        /// <summary>Rehydrate a Design (tree + seq + node-id cursor), or null if absent.</summary>
        public Design LoadDesign(DesignId id)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            string name; long rootId, nextId, appliedSeq;
            using (var head = new NpgsqlCommand(
                "SELECT name, root_node_id, next_node_id, applied_seq FROM design WHERE id = @id;", conn))
            {
                head.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id.Value });
                using var hr = head.ExecuteReader();
                if (!hr.Read()) return null;
                name = hr.GetString(0);
                rootId = hr.GetInt64(1);
                nextId = hr.GetInt64(2);
                appliedSeq = hr.GetInt64(3);
            }

            var nodes = new Dictionary<long, PartNode>();
            var childrenOf = new Dictionary<long, List<long>>();
            using (var nq = new NpgsqlCommand(
                "SELECT node_id, part_type, parent_node_id, attach, px,py,pz,qx,qy,qz,qw FROM design_node WHERE design_id = @id;", conn))
            {
                nq.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id.Value });
                using var nr = nq.ExecuteReader();
                while (nr.Read())
                {
                    var nid = nr.GetInt64(0);
                    var parent = nr.GetInt64(2);
                    var pose = new PartPose(nr.GetDouble(4), nr.GetDouble(5), nr.GetDouble(6),
                                            nr.GetDouble(7), nr.GetDouble(8), nr.GetDouble(9), nr.GetDouble(10));
                    nodes[nid] = new PartNode(new NodeId(nid), new PartTypeId(nr.GetString(1)),
                        new NodeId(parent), nr.GetString(3), pose);
                    if (!childrenOf.TryGetValue(parent, out var kids)) childrenOf[parent] = kids = new List<long>();
                    kids.Add(nid);
                }
            }

            if (!nodes.TryGetValue(rootId, out var root)) return null;
            var tree = new PartTree(root);
            // Add parent-before-child by walking from the root (ids alone aren't
            // safe to order after moves; the parent link is).
            AddChildren(tree, childrenOf, nodes, rootId);
            return Design.Restore(id, name, tree, nextId, appliedSeq);
        }

        private static void AddChildren(PartTree tree, Dictionary<long, List<long>> childrenOf,
            Dictionary<long, PartNode> nodes, long parentId)
        {
            if (!childrenOf.TryGetValue(parentId, out var kids)) return;
            foreach (var childId in kids)
            {
                tree.Add(nodes[childId]);
                AddChildren(tree, childrenOf, nodes, childId);
            }
        }
    }
}
