﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml.Linq;
using System.Xml;

namespace Synthesia
{
    /// <summary>Non-destructive Synthesia metadata XML reader/writer</summary>
    /// <remarks>Custom XML structures in existing metadata files will be preserved as best as possible.</remarks>
    public class MetadataFile
    {
        XDocument m_document;

        /// <summary>
        /// Starts a new, empty metadata XML file
        /// </summary>
        public MetadataFile()
        {
            m_document = new XDocument(new XElement("SynthesiaMetadata", (new XAttribute("Version", "1"))));
        }

        public MetadataFile(Stream input)
        {
            using (var reader = new StreamReader(input))
                m_document = XDocument.Load(reader, LoadOptions.None);

            XElement top = m_document.Root;
            if (top == null || top.Name != "SynthesiaMetadata") throw new InvalidOperationException("Stream does not contain a valid Synthesia metadata file.");
            
            if (top.AttributeOrDefault("Version") != "1") throw new InvalidOperationException("Unknown Synthesia metadata version.  A newer version of this editor may be available.");
        }

        public void Save(Stream output)
        {
            using (StreamWriter writer = new StreamWriter(output))
                m_document.Save(writer, SaveOptions.None);
        }

        public void RemoveSong(string uniqueId)
        {
            if (uniqueId == null) return;

            XElement songs = m_document.Root.Element("Songs");
            if (songs == null) return;

            foreach (XElement s in songs.Elements("Song"))
            {
                if (s.AttributeOrDefault("UniqueId") != uniqueId) continue;

                s.Remove();
                break;
            }
        }

        public void AddSong(XElement songs, SongEntry entry)
        {
            XElement element = (from e in songs.Elements("Song") where e.AttributeOrDefault("UniqueId") == entry.UniqueId select e).FirstOrDefault();
            if (element == null) songs.Add(element = new XElement("Song"));

            element.SetAttributeValue("UniqueId", entry.UniqueId);
            element.SetAttributeValue("Title", entry.Title);
            element.SetAttributeValue("Subtitle", entry.Subtitle);

            element.SetAttributeValue("Composer", entry.Composer);
            element.SetAttributeValue("Arranger", entry.Arranger);
            element.SetAttributeValue("Copyright", entry.Copyright);
            element.SetAttributeValue("License", entry.License);

            element.SetAttributeValue("Rating", entry.Rating);
            element.SetAttributeValue("Difficulty", entry.Difficulty);

            element.SetAttributeValue("FingerHints", entry.FingerHints);
            element.SetAttributeValue("HandParts", entry.HandParts);
            element.SetAttributeValue("Tags", string.Join(";", entry.Tags.ToArray()));
        }

        public void AddSong(SongEntry entry)
        {
            XElement songs = m_document.Root.Element("Songs");
            if (songs == null) m_document.Root.Add(songs = new XElement("Songs"));

            AddSong(songs, entry);
        }

        public IEnumerable<SongEntry> Songs
        {
            get
            {
                XElement songs = m_document.Root.Element("Songs");
                if (songs == null) yield break;

                foreach (XElement s in songs.Elements("Song"))
                {
                    SongEntry entry = new SongEntry();

                    entry.UniqueId = s.AttributeOrDefault("UniqueId");
                    entry.Title = s.AttributeOrDefault("Title");
                    entry.Subtitle = s.AttributeOrDefault("Subtitle");

                    entry.Composer = s.AttributeOrDefault("Composer");
                    entry.Arranger = s.AttributeOrDefault("Arranger");
                    entry.Copyright = s.AttributeOrDefault("Copyright");
                    entry.License = s.AttributeOrDefault("License");

                    entry.FingerHints = s.AttributeOrDefault("FingerHints");
                    entry.HandParts = s.AttributeOrDefault("HandParts");

                    int rating;
                    if (int.TryParse(s.AttributeOrDefault("Rating"), out rating)) entry.Rating = rating;

                    int difficulty;
                    if (int.TryParse(s.AttributeOrDefault("Difficulty"), out difficulty)) entry.Difficulty = difficulty;

                    string tags = s.AttributeOrDefault("Tags");
                    if (tags != null)
                    {
                        entry.ClearAllTags();
                        foreach (var t in tags.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                            entry.AddTag(t);
                    }

                    yield return entry;
                }
            }

            set
            {
                XElement songs = m_document.Root.Element("Songs");
                if (songs == null) m_document.Add(songs = new XElement("Songs"));

                foreach (SongEntry entry in value) AddSong(songs, entry);
            }

        }

        private GroupEntry RecursiveGroupLoad(XElement element)
        {
            GroupEntry entry = new GroupEntry() { Name = element.AttributeOrDefault("Name") };
            foreach (XElement s in element.Elements("Song")) entry.Songs.Add(new SongEntry() { UniqueId = s.AttributeOrDefault("UniqueId") });
            foreach (XElement g in element.Elements("Group")) entry.Groups.Add(RecursiveGroupLoad(g));

            return entry;
        }

        private XElement GroupFromPath(List<string> groupNamePath)
        {
            if (groupNamePath.Count == 0) return null;
            XElement result = m_document.Root.Element("Groups");

            foreach (string name in groupNamePath)
            {
                if (result == null) break;
                result = (from e in result.Elements("Group") where e.AttributeOrDefault("Name") == name select e).FirstOrDefault();
            }

            return result;
        }

        private void ValidatePath(List<string> groupNamePath)
        {
            if (groupNamePath.Count == 0) throw new InvalidOperationException("Group path cannot be empty.");
            foreach (string n in groupNamePath) if (string.IsNullOrWhiteSpace(n)) throw new InvalidOperationException("Group names cannot be empty.");
        }

        private string DisambiguateName(XElement parent, string desiredName)
        {
            if (parent == null) throw new InvalidOperationException("Bad parent.");
            if (string.IsNullOrWhiteSpace(desiredName)) throw new InvalidOperationException("Empty name.");

            int attempt = 1;
            string finalName = desiredName;
            while ((from e in parent.Elements("Group") where e.AttributeOrDefault("Name") == finalName select e).Any()) finalName = string.Format("{0} {1}", desiredName, ++attempt);

            return finalName;
        }

        public void RemoveGroup(List<string> groupNamePath)
        {
            ValidatePath(groupNamePath);

            XElement e = GroupFromPath(groupNamePath);
            if (e != null) e.Remove();
        }

        /// <summary>
        /// The path up through groupNamePath.Count-1 must already exist.  Name
        /// will be altered if a duplicate exists at the same level.  Final name
        /// is returned.
        /// </summary>
        public string AddGroup(List<string> groupNamePath)
        {
            ValidatePath(groupNamePath);

            XElement parent = groupNamePath.Count == 1 ? m_document.Root.Element("Groups") : GroupFromPath(groupNamePath.Take(groupNamePath.Count-1).ToList());
            if (parent == null) throw new InvalidOperationException(string.Format("All but the last element must already exist when adding a group.  Path: {0}", string.Join("/", groupNamePath)));

            string name = DisambiguateName(parent, groupNamePath.Last());
            parent.Add(new XElement("Group", new XAttribute("Name", name)));

            return name;
        }

        /// <summary>
        /// newName will be altered if a duplicate exists at the same
        /// level.  Final name is returned.
        /// </summary>
        public string RenameGroup(List<string> groupNamePath, string newName)
        {
            ValidatePath(groupNamePath);
            if (groupNamePath.Last() == newName) return newName;

            XElement group = GroupFromPath(groupNamePath);
            if (group == null) throw new InvalidOperationException("Couldn't find group to rename.");

            XElement parent = group.Parent;

            string name = DisambiguateName(parent, newName);
            group.SetAttributeValue("Name", name);

            return name;
        }

        public void SwapGroups(List<string> parentPath, string groupA, string groupB)
        {
            List<string> groupPathA = parentPath; groupPathA.Add(groupA);
            List<string> groupPathB = parentPath; groupPathB.Add(groupB);
            ValidatePath(groupPathA);
            ValidatePath(groupPathB);

            XElement a = GroupFromPath(groupPathA);
            XElement b = GroupFromPath(groupPathB);
            if (a == null || b == null) throw new InvalidOperationException("Couldn't find a group for swapping.");

            a.ReplaceWith(b);
            b.ReplaceWith(a);
        }

        public void AddSongToGroup(List<string> groupNamePath, string songUniqueId)
        {
            ValidatePath(groupNamePath);
            if (string.IsNullOrWhiteSpace(songUniqueId)) throw new InvalidOperationException("Bad song UniqueId");

            XElement g = GroupFromPath(groupNamePath);
            if ((from e in g.Elements("Song") where e.AttributeOrDefault("UniqueId") == songUniqueId select true).Any()) return;

            g.Add(new XElement("Song", new XAttribute("UniqueId", songUniqueId)));
        }

        public void RemoveSongFromGroup(List<string> groupNamePath, string songUniqueId)
        {
            ValidatePath(groupNamePath);
            if (string.IsNullOrWhiteSpace(songUniqueId)) throw new InvalidOperationException("Bad song UniqueId");

            XElement g = GroupFromPath(groupNamePath);
            foreach (XElement s in (from e in g.Elements("Song") where e.AttributeOrDefault("UniqueId") == songUniqueId select e)) s.Remove();
        }

        /// <remarks>More convenience than anything.  Maybe a little for efficiency, too.</remarks>
        public void RemoveAllSongsFromGroup(List<string> groupNamePath)
        {
            ValidatePath(groupNamePath);

            XElement g = GroupFromPath(groupNamePath);
            foreach (XElement s in (from e in g.Elements("Song") select e)) s.Remove();
        }

        public IEnumerable<GroupEntry> Groups
        {
            get
            {
                XElement groups = m_document.Root.Element("Groups");
                if (groups == null) yield break;

                foreach (XElement g in groups.Elements("Group")) yield return RecursiveGroupLoad(g);
            }
        }

    }
}
