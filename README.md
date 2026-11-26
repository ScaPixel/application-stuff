# Application stuff

## Purpose of this repo

This repository is a heavily sanitized version of my private project that I am working on currently.
It is missing plenty of the code, so it won't run, but it should showcase part of the networking that I am relying on for my project.
I have also renamed things, so hopefully it keeps my proprietary code safe while allowing me to showcase some of my skills.

I would add that this is a bit of a learning and experimental project, that has certain design choices I now would modify slightly.

I am happy with the project, and the real version even more so.

## Signaling Server

The signaling server is mostly correct compared to my current project, and is a server used to host on the cloud for orchestration of sessions and finding peers.

It is made in Rust and assists with basic nat discovery.
It also registers sessions, and sends signals about when the session is ready to start.
I have attempted to make it work both in peer-peer and client-host scenarios, but the focus was peer-peer so there are portions of how this works that require a bit of a workaround in client-host situations.

## dotnet-peer2peer-networking

Most of my peer to peer networking solution that I use when locating and creating sessions between peers.

Also contains a client project to show some of the project organization.

Missing the actual session network logic, and the reliability is not quite up to date.
The version here assumes a 1-1 amount of messages being sent both ways, while my real version now doesn't need that to be the case.

# What about the real repository?

I am happy to provide my actual repo for anyone with a legitimate need to see it, but I prefer to keep WIP code private.

Especially since the goal is to use my code for a larger project.
